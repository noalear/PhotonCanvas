using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;
using PhotonCanvas.Core;

namespace PhotonCanvas.App.Addons;

/// <summary>An add-on the program knows about: found on disk, and maybe running.</summary>
public sealed class AddonEntry
{
    public required AddonManifest Manifest { get; init; }
    public string Id => Manifest.Id;
    /// <summary>Running now.</summary>
    public bool IsOn => Running is not null;
    /// <summary>Why it couldn't start (or null).</summary>
    public string? Problem { get; internal set; }
    internal Running? Running { get; set; }
}

/// <summary>A running add-on: its load context, its object and what it added.</summary>
sealed class Running(AddonLoadContext context, IAddon addon, AddonHost host)
{
    public AddonLoadContext Context { get; } = context;
    public IAddon Addon { get; } = addon;
    public AddonHost Host { get; } = host;
}

/// <summary>
/// Finds, starts, stops, installs and removes add-ons while the program runs.
/// Built-in add-ons sit in an "addons" folder beside the program; ones the person installs go in
/// the data folder's "addons" folder (one installed there with a built-in one's id replaces it).
/// Each runs in its own collectible <see cref="AssemblyLoadContext"/>, loaded from memory so its
/// files are never locked and can be replaced or deleted while the program runs.
/// </summary>
public sealed class AddonManager
{
    /// <summary>The add-on file extension for installing (a zip of the add-on's folder).</summary>
    public const string Extension = ".pcaddon";

    readonly IAddonServices _services;
    readonly Func<string, bool?> _chosen;
    readonly Action<string, bool> _choose;
    readonly List<AddonEntry> _entries = [];
    internal readonly Dictionary<Type, (object Service, string Owner)> Services = [];

    /// <summary>The add-ons that come with the program (PHOTONCANVAS_ADDONS_DIR points elsewhere, for tools and tests).</summary>
    public static string BuiltInFolder =>
        Environment.GetEnvironmentVariable("PHOTONCANVAS_ADDONS_DIR") is { Length: > 0 } custom ? custom : Path.Combine(AppContext.BaseDirectory, "addons");
    public static string UserFolder => Path.Combine(AppIdentity.DataDir, "addons");
    public static string DataRoot => Path.Combine(AppIdentity.DataDir, "addon-data");

    /// <param name="chosen">Has the person turned this add-on on (true) or off (false); null = never chosen.</param>
    /// <param name="choose">Remember the person's choice.</param>
    public AddonManager(IAddonServices services, Func<string, bool?> chosen, Action<string, bool> choose)
    {
        _services = services;
        _chosen = chosen;
        _choose = choose;
    }

    public IReadOnlyList<AddonEntry> Entries => _entries;

    /// <summary>Raised after any add-on starts, stops, is installed or removed.</summary>
    public event Action? Changed;

    /// <summary>Look for add-ons on disk (keeps running ones running).</summary>
    public void Scan()
    {
        var found = new Dictionary<string, AddonManifest>(StringComparer.OrdinalIgnoreCase);
        foreach (var (root, builtIn) in new[] { (BuiltInFolder, true), (UserFolder, false) })
        {
            if (!Directory.Exists(root)) continue;
            foreach (var dir in Directory.GetDirectories(root).Order(StringComparer.OrdinalIgnoreCase))
            {
                if (!File.Exists(Path.Combine(dir, "addon.json"))) continue;
                try
                {
                    var m = AddonManifest.Read(dir, builtIn);
                    found[m.Id] = m;
                }
                catch (Exception e) { AppLog.Write("addons", $"{dir}: {e.Message}"); }
            }
        }
        foreach (var gone in _entries.Where(e => !found.ContainsKey(e.Id) && !e.IsOn).ToList()) _entries.Remove(gone);
        foreach (var m in found.Values)
        {
            int at = _entries.FindIndex(e => e.Id == m.Id);
            if (at < 0) _entries.Add(new AddonEntry { Manifest = m });
            else if (!_entries[at].IsOn) _entries[at] = new AddonEntry { Manifest = m };
        }
        _entries.Sort((a, b) => string.Compare(a.Manifest.Name, b.Manifest.Name, StringComparison.CurrentCultureIgnoreCase));
    }

    /// <summary>Should this one be on: the person's choice, or else what it says it wants.</summary>
    public bool WantsOn(AddonEntry e) => _chosen(e.Id) ?? e.Manifest.OnByDefault;

    /// <summary>Start every add-on that should be on (at start-up), needed ones first.</summary>
    public void StartAll()
    {
        Scan();
        foreach (var e in _entries.Where(WantsOn).ToList()) Start(e, remember: false);
        Changed?.Invoke();
    }

    public AddonEntry? Find(string id) => _entries.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Turn an add-on on or off now (and remember the choice).</summary>
    public bool Set(string id, bool on)
    {
        if (Find(id) is not { } e) return false;
        bool ok = on ? Start(e, remember: true) : Stop(e, remember: true);
        Changed?.Invoke();
        return ok;
    }

    bool Start(AddonEntry e, bool remember, HashSet<string>? starting = null)
    {
        if (remember) _choose(e.Id, true);
        if (e.IsOn) return true;
        e.Problem = null;
        starting ??= new(StringComparer.OrdinalIgnoreCase);
        if (!starting.Add(e.Id)) { e.Problem = "It needs itself (its \"requires\" go round in a circle)."; return false; }
        if (e.Manifest.ApiVersion > AddonHost.CurrentApiVersion)
        {
            e.Problem = $"It needs a newer PhotonCanvas (add-on API {e.Manifest.ApiVersion}; this one has {AddonHost.CurrentApiVersion}).";
            return false;
        }
        foreach (var need in e.Manifest.Requires)
        {
            if (Find(need) is not { } dep) { e.Problem = $"It needs the add-on \"{need}\", which isn't installed."; return false; }
            if (!Start(dep, remember, starting)) { e.Problem = $"It needs \"{dep.Manifest.Name}\", which couldn't start: {dep.Problem}"; return false; }
        }
        AddonLoadContext? ctx = null;
        AddonHost? host = null;
        try
        {
            ctx = new AddonLoadContext(e.Manifest, this);
            var asm = ctx.LoadMain();
            var type = EntryType(asm, e.Manifest);
            var addon = (IAddon)Activator.CreateInstance(type)!;
            host = new AddonHost(this, _services, e.Manifest);
            addon.Start(host);
            e.Running = new Running(ctx, addon, host);
            AppLog.Write("addons", $"Started {e.Id} {e.Manifest.Version}");
            return true;
        }
        catch (Exception ex)
        {
            var inner = ex is TargetInvocationException { InnerException: { } i } ? i : ex;
            e.Problem = $"It couldn't start: {inner.Message}";
            AppLog.Error($"Add-on {e.Id}", inner);
            host?.RemoveAll();
            ctx?.Unload();
            return false;
        }
    }

    static Type EntryType(Assembly asm, AddonManifest m)
    {
        if (m.Entry is { Length: > 0 } name)
            return asm.GetType(name, throwOnError: false) ?? throw new InvalidOperationException($"There's no class {name} in {asm.GetName().Name}.");
        var types = asm.GetExportedTypes().Where(t => typeof(IAddon).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false }).ToList();
        return types.Count == 1 ? types[0]
            : throw new InvalidOperationException(types.Count == 0 ? "It has no class that is an IAddon." : "It has more than one IAddon class: name one under \"entry\" in addon.json.");
    }

    bool Stop(AddonEntry e, bool remember)
    {
        if (remember) _choose(e.Id, false);
        if (e.Running is not { } r) return true;
        // What needs this one stops first.
        foreach (var user in _entries.Where(x => x.IsOn && x.Manifest.Requires.Contains(e.Id, StringComparer.OrdinalIgnoreCase)).ToList())
            Stop(user, remember: false);
        try { r.Addon.Stop(); }
        catch (Exception ex) { AppLog.Error($"Add-on {e.Id} stopping", ex); }
        r.Host.RemoveAll();
        e.Running = null;
        var weak = new WeakReference(r.Context);
        r.Context.Unload();
        AvaloniaTypeCaches.Forget(r.Context);
        AppLog.Write("addons", $"Stopped {e.Id}");
        // Unloading finishes once nothing points into it any more; say in the log if it never does.
        _ = Task.Run(async () =>
        {
            for (int i = 0; i < 10 && weak.IsAlive; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                await Task.Delay(500);
            }
            if (weak.IsAlive) AppLog.Write("addons", $"{e.Id}: still in memory after stopping (something still points into it); it's off, and goes when the program closes.");
        });
        return true;
    }

    /// <summary>Stop everything (the program is closing).</summary>
    public void StopAll()
    {
        foreach (var e in _entries.Where(x => x.IsOn).Reverse().ToList()) Stop(e, remember: false);
    }

    /// <summary>
    /// Install (or update) from a .pcaddon file or a folder holding addon.json, and turn it on.
    /// Returns the add-on, or throws with a message for the person.
    /// </summary>
    public AddonEntry Install(string path)
    {
        string temp = Path.Combine(Path.GetTempPath(), "pcaddon-" + Guid.NewGuid().ToString("N"));
        try
        {
            string source;
            if (Directory.Exists(path)) source = path;
            else
            {
                ZipFile.ExtractToDirectory(path, temp);
                // Zipped with or without its folder.
                source = File.Exists(Path.Combine(temp, "addon.json")) ? temp
                    : Directory.GetDirectories(temp).FirstOrDefault(d => File.Exists(Path.Combine(d, "addon.json")))
                      ?? throw new InvalidOperationException("That file has no addon.json in it, so it isn't a PhotonCanvas add-on.");
            }
            var m = AddonManifest.Read(source, builtIn: false);
            if (Find(m.Id) is { IsOn: true } running) Stop(running, remember: false);
            var dest = Path.Combine(UserFolder, m.Id);
            if (Directory.Exists(dest)) Directory.Delete(dest, recursive: true);
            CopyFolder(source, dest);
            Scan();
            var e = Find(m.Id)!;
            Start(e, remember: true);
            Changed?.Invoke();
            return e;
        }
        finally
        {
            try { if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>Can it be deleted (one the person installed), or only turned off (built in).</summary>
    public static bool CanRemove(AddonEntry e) => !e.Manifest.BuiltIn;

    /// <summary>Stop it and delete it from disk. A built-in one under it (same id) shows again, off.</summary>
    public void Remove(string id)
    {
        if (Find(id) is not { } e) return;
        if (!CanRemove(e)) throw new InvalidOperationException($"\"{e.Manifest.Name}\" comes with PhotonCanvas: it can be turned off, not removed.");
        Stop(e, remember: false);
        Directory.Delete(e.Manifest.Folder, recursive: true);
        _entries.Remove(e);
        _choose(id, false);
        Scan();
        Changed?.Invoke();
    }

    static void CopyFolder(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)), overwrite: true);
        foreach (var d in Directory.GetDirectories(from)) CopyFolder(d, Path.Combine(to, Path.GetFileName(d)));
    }

    /// <summary>The assembly a running add-on loaded under this name (for add-ons that use another add-on's types).</summary>
    internal Assembly? FromOtherAddon(AssemblyName name, AddonLoadContext asking)
    {
        foreach (var e in _entries)
            if (e.Running is { } r && r.Context != asking && r.Context.Loaded(name) is { } a) return a;
        return null;
    }
}

/// <summary>
/// One add-on's assemblies. What the program itself ships (Avalonia, Core, Doc, the App) always
/// comes from the program, so the add-on shares its types; other add-ons' assemblies come from
/// those add-ons (for "requires"); the rest from the add-on's folder, read into memory.
/// </summary>
sealed class AddonLoadContext : AssemblyLoadContext
{
    static readonly HashSet<string> ProgramAssemblies = ((AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string) ?? "")
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .Select(Path.GetFileNameWithoutExtension).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);

    readonly AddonManifest _manifest;
    readonly AddonManager _manager;
    readonly Dictionary<string, Assembly> _mine = new(StringComparer.OrdinalIgnoreCase);

    public AddonLoadContext(AddonManifest manifest, AddonManager manager) : base($"addon:{manifest.Id}", isCollectible: true)
    {
        _manifest = manifest;
        _manager = manager;
        // The runtime holds a context firmly while it unloads, so what it still points to would keep
        // it in memory for good: let go of the assemblies once unloading starts.
        Unloading += _ => _mine.Clear();
    }

    public Assembly LoadMain()
    {
        var file = _manifest.Assembly is { Length: > 0 } a ? Path.Combine(_manifest.Folder, a)
            : Directory.GetFiles(_manifest.Folder, "PhotonCanvas.Addon.*.dll").SingleOrDefault()
              ?? throw new InvalidOperationException("addon.json doesn't say which .dll to load (\"assembly\"), and there isn't exactly one PhotonCanvas.Addon.*.dll.");
        return FromFile(file);
    }

    public Assembly? Loaded(AssemblyName name) => _mine.GetValueOrDefault(name.Name ?? "");

    Assembly FromFile(string file)
    {
        // From memory, so the file stays free to be replaced or deleted.
        using var dll = new MemoryStream(File.ReadAllBytes(file));
        var pdbFile = Path.ChangeExtension(file, ".pdb");
        using var pdb = File.Exists(pdbFile) ? new MemoryStream(File.ReadAllBytes(pdbFile)) : null;
        var asm = LoadFromStream(dll, pdb);
        _mine[asm.GetName().Name!] = asm;
        return asm;
    }

    protected override Assembly? Load(AssemblyName name)
    {
        // The program's own (and .NET's): shared, so the types are the same ones.
        if (name.Name is { } n && (ProgramAssemblies.Contains(n) || Default.Assemblies.Any(a => a.GetName().Name == n))) return null;
        if (Loaded(name) is { } mine) return mine;
        if (_manager.FromOtherAddon(name, this) is { } theirs) return theirs;
        var file = Path.Combine(_manifest.Folder, name.Name + ".dll");
        return File.Exists(file) ? FromFile(file) : null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        foreach (var candidate in new[] { unmanagedDllName, unmanagedDllName + ".dll", "lib" + unmanagedDllName + ".so", "lib" + unmanagedDllName + ".dylib" })
        {
            var file = Path.Combine(_manifest.Folder, candidate);
            if (File.Exists(file)) return LoadUnmanagedDllFromPath(file);
        }
        return IntPtr.Zero;
    }
}
