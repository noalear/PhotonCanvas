using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using PhotonCanvas.App.Addons;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.App;

/// <summary>
/// New versions of the program and of installed add-ons: the check (at start and every few hours),
/// downloading in the background, the "a new version is out" window, the ready-to-restart button in
/// the status line, and the Updates page in Settings. The swapping itself is <see cref="UpdateInstall"/>,
/// done by <see cref="AtExit"/> when the program closes.
/// </summary>
public sealed class Updater
{
    readonly AppSettings _settings;
    readonly Window _main;
    readonly Func<AddonManager?> _addons;
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromHours(6) };
    bool _busy;

    public Updater(AppSettings settings, Window main, Func<AddonManager?> addons)
    {
        _settings = settings;
        _main = main;
        _addons = addons;
        _timer.Tick += (_, _) => { if (_settings.CheckForUpdates) _ = Check(manual: false); };
    }

    /// <summary>A version downloaded and waiting to go in (when the program closes).</summary>
    public string? Ready { get; private set; }
    /// <summary>The version being downloaded now, and how far it's got (0–1).</summary>
    public string? Downloading { get; private set; }
    public double Progress { get; private set; }
    /// <summary>Just updated to this version (shown for a little while after the first start).</summary>
    public string? JustUpdated { get; private set; }
    /// <summary>Something changed that the status-line button shows.</summary>
    public event Action? Changed;

    /// <summary>The program to start once this one has closed (Restart now, or a freshly installed copy).</summary>
    public static string? StartAfterClose { get; set; }

    static string ErrorFile => Path.Combine(UpdateInstall.UpdatesDir, "last-error.txt");

    /// <summary>The program has opened (for real, not in a test): tidy up after the last update, say if it went in, and start checking.</summary>
    public void Begin()
    {
        if (UpdateInstall.PendingIsStale) UpdateInstall.ClearDownloads(); // this version, or a newer one, is already in
        Ready = UpdateInstall.PendingHere?.Version;
        // Only published copies count (a copy built from the source code shares the settings).
        if (UpdateInstall.Marker is not null)
        {
            if (_settings.LastVersion is { } last && Updates.Newer(Updates.Current, last))
            {
                JustUpdated = Updates.Current;
                AppLog.Write("updates", $"Now running {Updates.Current} (was {last}).");
                DispatcherTimer.RunOnce(() => { JustUpdated = null; Changed?.Invoke(); }, TimeSpan.FromSeconds(60));
            }
            if (_settings.LastVersion != Updates.Current) { _settings.LastVersion = Updates.Current; _settings.Save(); }
        }
        Changed?.Invoke();
        _ = Task.Run(async () =>
        {
            // The old files are only free once the copy that swapped them has finished closing.
            await Task.Delay(TimeSpan.FromSeconds(20));
            if (UpdateInstall.Marker is not null && UpdateInstall.CleanUp(UpdateInstall.InstallRoot) is > 0 and var n)
                AppLog.Write("updates", $"Deleted {n} file(s) left over from the last update.");
            if (UpdateInstall.Pending is null) UpdateInstall.ClearDownloads();
        });
        string? failed = null;
        try
        {
            if (File.Exists(ErrorFile)) { failed = File.ReadAllText(ErrorFile); File.Delete(ErrorFile); }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        if (failed is not null) _ = Dialogs.Message(_main, "Updates", failed);
        else if (_settings.CheckForUpdates) _ = Check(manual: false);
        _timer.Start();
    }

    public void End() => _timer.Stop();

    // ---- checking ---------------------------------------------------------------------------------

    /// <summary>Ask GitHub for a newer version of the program and of installed add-ons. Quiet unless <paramref name="manual"/> (Check now) or there's something to say.</summary>
    public async Task Check(bool manual)
    {
        if (_busy)
        {
            if (manual) await Dialogs.Message(_main, "Updates", Downloading is { } d ? $"PhotonCanvas {d} is downloading ({Progress:0%})." : "Already checking.");
            return;
        }
        _busy = true;
        try
        {
            var token = UpdateInstall.ReadToken();
            var answer = await Updates.LatestAsync(null, token);
            var r = answer.Release;
            if (r is null || !Updates.Newer(r.Version, Updates.Current))
            {
                if (manual) await Dialogs.Message(_main, "Updates", r is null ? Trouble(answer.Outcome, Updates.Repo, token) : $"{AppIdentity.ProductName} {Updates.Current} is the latest version.");
            }
            else if (Ready == r.Version)
            {
                if (manual) await ReadyWindow(r.Version);
            }
            else if (!manual && r.Version == _settings.SkippedVersion) { }
            else if (!manual && r.ForThisComputer is null) { } // nothing published for this computer: only Check now says so
            else if (UpdateInstall.CanUpdateInPlace && r.ForThisComputer is { } file && !manual && _settings.AutoUpdate)
            {
                try { await Fetch(r, file, token, null); }
                catch (Exception e) when (IsTrouble(e)) { AppLog.Write("updates", $"Couldn't download {r.Version}: {e.Message}"); }
            }
            else await Offer(r, token);

            await CheckAddons(manual, token);
        }
        finally
        {
            _busy = false;
            Changed?.Invoke();
        }
    }

    static bool IsTrouble(Exception e) => e is IOException or HttpRequestException or UnauthorizedAccessException or InvalidDataException
        or TaskCanceledException or OperationCanceledException or FormatException;

    static string Trouble(Updates.Outcome o, string repo, string? token) => o switch
    {
        Updates.Outcome.Offline => "Couldn't reach GitHub to check (offline?).",
        Updates.Outcome.KeyRefused => "GitHub turned the access key down (it may have expired). Put a new one in Settings → Updates.",
        _ when token is null => $"No versions found at github.com/{repo}. Either none has been published yet, or the repository is private: then {AppIdentity.ProductName} needs a GitHub access key (Settings → Updates).",
        _ => $"No versions found at github.com/{repo}: none has been published yet, or the access key can't read that repository.",
    };

    // ---- downloading ------------------------------------------------------------------------------

    /// <summary>Download a version, check it against the release's SHA256SUMS and unpack it into the data folder. Returns the unpacked version's folder.</summary>
    async Task<string> Download(Updates.Release r, Updates.Asset file, string? token, string into, IProgress<double>? progress)
    {
        var sums = r.Named(Updates.SumsFile) ?? throw new InvalidDataException($"The release has no {Updates.SumsFile}, so the download couldn't be checked: not installing it.");
        var expected = Updates.ParseSums(await Updates.DownloadTextAsync(sums, token)).GetValueOrDefault(file.Name)
            ?? throw new InvalidDataException($"{Updates.SumsFile} doesn't list {file.Name}, so it couldn't be checked: not installing it.");
        Downloading = r.Version;
        Progress = 0;
        Changed?.Invoke();
        var archive = Path.Combine(into, file.Name);
        try
        {
            var report = new Progress<double>(p => { Progress = p; progress?.Report(p); Changed?.Invoke(); });
            await Updates.DownloadAsync(file, archive, token, report);
            if (await Task.Run(() => Updates.Sha256(archive)) != expected)
                throw new InvalidDataException("The download doesn't match the release's checksum (damaged on the way?), so it wasn't installed.");
            var root = await Task.Run(() => UpdateInstall.Unpack(archive, Path.Combine(into, "files")));
            if (UpdateInstall.ReadMarker(UpdateInstall.ProgramDirIn(root)) is null)
                throw new InvalidDataException("The download isn't a published PhotonCanvas (it has no update.json).");
            return root;
        }
        finally
        {
            try { File.Delete(archive); } catch (IOException) { }
            Downloading = null;
            Changed?.Invoke();
        }
    }

    /// <summary>Download a version and leave it to go in when the program closes.</summary>
    async Task Fetch(Updates.Release r, Updates.Asset file, string? token, IProgress<double>? progress)
    {
        UpdateInstall.ClearDownloads();
        var into = Path.Combine(UpdateInstall.UpdatesDir, r.Version);
        var root = await Download(r, file, token, into, progress);
        UpdateInstall.SetPending(new(r.Version, root, UpdateInstall.InstallRoot));
        Ready = r.Version;
        AppLog.Write("updates", $"{r.Version} downloaded; it goes in when {AppIdentity.ProductName} closes.");
        Changed?.Invoke();
    }

    /// <summary>Close (asking to save as usual) and start again with the new version.</summary>
    public void RestartNow()
    {
        StartAfterClose = Environment.ProcessPath;
        _main.Close();
    }

    // ---- the windows ------------------------------------------------------------------------------

    /// <summary>A newer version is out: what's new, and Update (or Install, or the download page), Skip this version, or Later.</summary>
    async Task Offer(Updates.Release r, string? token)
    {
        var file = r.ForThisComputer;
        bool inPlace = UpdateInstall.CanUpdateInPlace && file is not null;
        bool installCopy = !inPlace && UpdateInstall.Marker is null && OperatingSystem.IsWindows() && file is not null;
        var w = new Window
        {
            Title = "A new version is out",
            Width = 560, SizeToContent = SizeToContent.Height, MaxHeight = 680,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush("Bg1"), CanResize = false, ShowInTaskbar = false,
        };
        var main = new Button
        {
            Content = inPlace ? "Update" : installCopy ? "Install it" : "Open the download page",
            Classes = { "primary" }, MinWidth = 120,
        };
        var skip = new Button { Content = "Skip this version", MinWidth = 120 };
        var later = new Button { Content = "Later", IsCancel = true, MinWidth = 90 };
        var bar = new ProgressBar { Minimum = 0, Maximum = 1, IsVisible = false, Height = 6 };
        var status = Hint();
        string about = $"You have {Updates.Current}. " + (inPlace
            ? $"Update downloads it ({Size(file!.Size)}) and checks it; it goes in when you close {AppIdentity.ProductName}, or straight away with Restart now. Your designs, settings and library are kept."
            : installCopy
            ? $"This copy was built from the source code, so it doesn't change itself. Install it puts the published version in {InstallFolder} with shortcuts on the desktop and in the Start menu; that copy keeps itself up to date. Your designs, settings and library are shared, so nothing is lost."
            : file is { } f
            ? $"The download for this computer is {f.Name}: unpack it in place of the old one (your designs, settings and library are kept in your PhotonCanvas folder, not with the program)."
            : "There's no download for this computer in it; the page has the files there are.");
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { later, skip, main } };
        skip.Click += (_, _) => { _settings.SkippedVersion = r.Version; _settings.Save(); w.Close(); };
        later.Click += (_, _) => w.Close();
        main.Click += async (_, _) =>
        {
            if (!inPlace && !installCopy) { Open(file?.Url is { Length: > 0 } u ? u : r.PageUrl); w.Close(); return; }
            main.IsEnabled = skip.IsEnabled = false;
            bar.IsVisible = true;
            status.Text = "Downloading…";
            var progress = new Progress<double>(p => { bar.Value = p; status.Text = $"Downloading… {p:0%}"; });
            try
            {
                if (inPlace)
                {
                    await Fetch(r, file!, token, progress);
                    w.Close();
                    await ReadyWindow(r.Version);
                }
                else
                {
                    await InstallCopy(r, file!, token, progress);
                    w.Close();
                }
            }
            catch (Exception e) when (IsTrouble(e))
            {
                AppLog.Write("updates", $"{r.Version}: {e.Message}");
                bar.IsVisible = false;
                status.Text = "Couldn't do it: " + e.Message;
                main.IsEnabled = skip.IsEnabled = true;
            }
        };
        w.Content = new StackPanel
        {
            Spacing = 10, Margin = new(18),
            Children =
            {
                new TextBlock { Text = $"{AppIdentity.ProductName} {r.Version} is out", FontSize = 18, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = about, TextWrapping = TextWrapping.Wrap },
                new ScrollViewer { MaxHeight = 300, Content = new TextBlock { Text = r.Notes.Trim().Length > 0 ? r.Notes.Trim() : "(No notes for this version.)", TextWrapping = TextWrapping.Wrap, FontSize = 12.5 } },
                bar, status, buttons,
            },
        };
        await w.ShowDialog(_main);
    }

    /// <summary>Downloaded: Restart now, or let it go in at the next close.</summary>
    async Task ReadyWindow(string version)
    {
        if (await Dialogs.Confirm(_main, "Update ready",
                $"{AppIdentity.ProductName} {version} is downloaded and checked. It goes in when you close {AppIdentity.ProductName}; Restart now does that straight away (you're asked to save first, as usual).",
                "Restart now", "When I close"))
            RestartNow();
    }

    static string Size(long bytes) => bytes <= 0 ? "a few tens of MB" : $"{bytes / 1048576.0:0} MB";

    static void Open(string url)
    {
        if (string.IsNullOrEmpty(url)) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { AppLog.Write("updates", e.Message); }
    }

    // ---- a copy built from the source code: install the published one beside it --------------------

    /// <summary>Where a copy built from the source code installs the published version (Windows).</summary>
    static string InstallFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppIdentity.ProductName);

    async Task InstallCopy(Updates.Release r, Updates.Asset file, string? token, IProgress<double> progress)
    {
        var into = Path.Combine(UpdateInstall.UpdatesDir, "install-" + r.Version);
        try
        {
            var root = await Download(r, file, token, into, progress);
            var target = InstallFolder;
            await Task.Run(() => UpdateInstall.InstallInto(root, target));
            var exe = Path.Combine(target, AppIdentity.ProductName + ".exe");
            await Task.Run(() => MakeShortcuts(exe));
            AppLog.Write("updates", $"Installed {r.Version} in {target}.");
            if (await Dialogs.Confirm(_main, "Installed",
                    $"{AppIdentity.ProductName} {r.Version} is installed in {target}, with shortcuts on the desktop and in the Start menu. Open it from there from now on: that copy keeps itself up to date.\n\nOpen it now? This copy closes (you're asked to save first, as usual).",
                    "Open it now", "Not now"))
            {
                StartAfterClose = exe;
                _main.Close();
            }
        }
        finally
        {
            try { if (Directory.Exists(into)) Directory.Delete(into, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>"PhotonCanvas" shortcuts in the Start menu and on the desktop (Windows; best effort).</summary>
    static void MakeShortcuts(string exe)
    {
        if (!OperatingSystem.IsWindows()) return;
        static string Q(string s) => "'" + s.Replace("'", "''") + "'";
        var dir = Path.GetDirectoryName(exe)!;
        var links = new[] { Environment.SpecialFolder.Programs, Environment.SpecialFolder.DesktopDirectory }
            .Select(f => Environment.GetFolderPath(f)).Where(d => d.Length > 0)
            .Select(d => Path.Combine(d, AppIdentity.ProductName + ".lnk"));
        var script = "$w = New-Object -ComObject WScript.Shell; " + string.Concat(links.Select(l =>
            $"$s = $w.CreateShortcut({Q(l)}); $s.TargetPath = {Q(exe)}; $s.WorkingDirectory = {Q(dir)}; $s.Save(); "));
        try
        {
            var psi = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
            foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script }) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            p?.WaitForExit(20000);
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { AppLog.Write("updates", "Couldn't make shortcuts: " + e.Message); }
    }

    // ---- add-ons ----------------------------------------------------------------------------------

    sealed record AddonNews(AddonEntry Entry, Updates.Release Release, Updates.Asset File);

    /// <summary>Installed add-ons whose addon.json names an "updates" repository: put newer releases in (asks first unless updates go in by themselves).</summary>
    async Task CheckAddons(bool manual, string? token)
    {
        if (_addons() is not { } mgr) return;
        var news = new List<AddonNews>();
        foreach (var e in mgr.Entries.Where(e => !e.Manifest.BuiltIn && Updates.IsRepo(e.Manifest.Updates)).ToList())
        {
            var r = (await Updates.LatestAsync(e.Manifest.Updates, token)).Release;
            if (r is not null && Updates.Newer(r.Version, e.Manifest.Version) && r.AddonFile(e.Id) is { } f) news.Add(new(e, r, f));
        }
        if (news.Count == 0) return;
        if (manual || !_settings.AutoUpdate)
        {
            var list = string.Join("\n", news.Select(n => $"• {n.Entry.Manifest.Name}: {n.Entry.Manifest.Version} → {n.Release.Version}"));
            if (!await Dialogs.Confirm(_main, "Add-on updates", $"New versions of add-ons you installed:\n\n{list}\n\nUpdate them now? Each one is stopped and started again (its windows close).", "Update", "Not now")) return;
        }
        var done = new List<string>();
        foreach (var n in news)
        {
            var path = Path.Combine(UpdateInstall.UpdatesDir, "addons", n.File.Name);
            try
            {
                await Updates.DownloadAsync(n.File, path, token);
                if (n.Release.Named(Updates.SumsFile) is { } sums)
                {
                    var expected = Updates.ParseSums(await Updates.DownloadTextAsync(sums, token)).GetValueOrDefault(n.File.Name);
                    if (expected is null || await Task.Run(() => Updates.Sha256(path)) != expected)
                        throw new InvalidDataException("the download doesn't match its checksum");
                }
                var e = mgr.Install(path, keepOnOff: true);
                AppLog.Write("updates", $"Add-on {e.Id} updated to {e.Manifest.Version}.");
                done.Add($"{e.Manifest.Name} {e.Manifest.Version}");
            }
            catch (Exception ex) when (IsTrouble(ex) || ex is System.Text.Json.JsonException)
            {
                AppLog.Write("updates", $"Add-on {n.Entry.Id}: couldn't update to {n.Release.Version}: {ex.Message}");
                done.Add($"{n.Entry.Manifest.Name}: couldn't update ({ex.Message})");
            }
            finally
            {
                try { File.Delete(path); } catch (IOException) { }
            }
        }
        if (manual) await Dialogs.Message(_main, "Add-on updates", string.Join("\n", done));
    }

    // ---- the status line --------------------------------------------------------------------------

    /// <summary>Show what's going on in the status line's button (hidden when there's nothing).</summary>
    public void ShowIn(Button chip)
    {
        void Refresh()
        {
            (string? text, string? tip) = Downloading is { } d ? ($"Downloading {AppIdentity.ProductName} {d}… {Progress:0%}", "A new version is downloading in the background.")
                : Ready is { } v ? ($"{AppIdentity.ProductName} {v} is ready: restart", $"Close and start again with {v} (you're asked to save first). Or it goes in the next time you close {AppIdentity.ProductName}.")
                : JustUpdated is { } u ? ($"Updated to {u}: what's new", "The release notes for this version, on GitHub.")
                : (null, null);
            chip.IsVisible = text is not null;
            chip.Content = text;
            chip.IsEnabled = Downloading is null;
            ToolTip.SetTip(chip, tip);
        }
        chip.Click += (_, _) =>
        {
            if (Ready is not null) RestartNow();
            else if (JustUpdated is { } u) Open($"https://github.com/{Updates.Repo}/releases/tag/v{u}");
        };
        Changed += () => Dispatcher.UIThread.Post(Refresh);
        Refresh();
    }

    // ---- Settings → Updates -----------------------------------------------------------------------

    /// <summary>Settings → Updates: this version, checking and installing on or off, Check now, and the GitHub access key.</summary>
    public Control Panel()
    {
        var check = new CheckBox { Content = "Check for a new version at start", IsChecked = _settings.CheckForUpdates };
        check.IsCheckedChanged += (_, _) => { _settings.CheckForUpdates = check.IsChecked == true; _settings.Save(); };
        var auto = new CheckBox { Content = "Put new versions in by themselves", IsChecked = _settings.AutoUpdate };
        auto.IsCheckedChanged += (_, _) => { _settings.AutoUpdate = auto.IsChecked == true; _settings.Save(); };
        var now = new Button { Content = "Check now", MinWidth = 140, HorizontalAlignment = HorizontalAlignment.Left };
        now.Click += async (_, _) => await Check(manual: true);

        var key = new TextBox { PasswordChar = '•', Watermark = UpdateInstall.ReadToken() is null ? "github_pat_…" : "(saved: paste a new one to replace it)", Width = 320 };
        var saveKey = new Button { Content = "Save key", MinWidth = 90 };
        var forget = new Button { Content = "Forget key", MinWidth = 90, IsVisible = UpdateInstall.ReadToken() is not null };
        var keyStatus = Hint();
        saveKey.Click += (_, _) =>
        {
            try
            {
                UpdateInstall.SaveToken(key.Text);
                key.Text = "";
                bool has = UpdateInstall.ReadToken() is not null;
                key.Watermark = has ? "(saved: paste a new one to replace it)" : "github_pat_…";
                forget.IsVisible = has;
                keyStatus.Text = has ? "Saved. Check now to try it." : "No key saved.";
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { keyStatus.Text = "Couldn't save it: " + e.Message; }
        };
        forget.Click += (_, _) =>
        {
            try { UpdateInstall.SaveToken(null); forget.IsVisible = false; key.Watermark = "github_pat_…"; keyStatus.Text = "Key forgotten."; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { keyStatus.Text = "Couldn't delete it: " + e.Message; }
        };

        string where = UpdateInstall.Marker is null
            ? " It was built from the source code, so it never changes itself: when there's a new version you can install the published one beside it, which does."
            : !UpdateInstall.CanUpdateInPlace ? $" Its folder ({UpdateInstall.InstallRoot}) can't be written to, so it can't update itself: move it somewhere of your own (your Documents, or AppData\\Local\\Programs)." : "";
        var panel = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = "UPDATES", Classes = { "h" } },
                new TextBlock { Text = $"This is {AppIdentity.ProductName} {Updates.Current} ({Updates.Platform}).{where}", TextWrapping = TextWrapping.Wrap },
                check,
                auto,
                Hint($"New versions are downloaded in the background and checked against the release's checksums, then put in the next time you close {AppIdentity.ProductName} (or straight away with the restart button at the bottom of the window). Add-ons you installed that say where their updates come from are updated too. Off: you're asked first."),
                now,
                Hint($"{AppIdentity.ProductName} asks GitHub (github.com/{Updates.Repo}) for the latest version at start and every few hours. Nothing about you or your designs is sent; like any web request it does reveal your internet address to GitHub. Unticking the first box: never asks, except when you click Check now."),
                new TextBlock { Text = "GITHUB ACCESS KEY", Classes = { "h" }, Margin = new(0, 8, 0, 0) },
                Hint($"Only needed while new versions are published in a private GitHub repository. On github.com: Settings → Developer settings → Personal access tokens → Fine-grained tokens → Generate new token; pick the repository ({Updates.Repo}), give it \"Contents: Read-only\" and nothing else, and paste it here. It's kept on this computer only (github-key.txt in your data folder, never in backups) and only ever sent to api.github.com."),
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { key, saveKey, forget } },
                keyStatus,
            },
        };
        if (Ready is { } v)
        {
            var restart = new Button { Content = "Restart now", Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Left };
            restart.Click += (_, _) => RestartNow();
            panel.Children.Insert(2, new StackPanel { Spacing = 6, Children = { new TextBlock { Text = $"{AppIdentity.ProductName} {v} is downloaded: it goes in when you close {AppIdentity.ProductName}.", TextWrapping = TextWrapping.Wrap }, restart } });
        }
        return panel;
    }

    // ---- when the program closes ------------------------------------------------------------------

    /// <summary>
    /// The program has closed: put a downloaded version in (unless another copy from the same folder
    /// is still open), then start again if asked to. Called from Main after the window is gone.
    /// </summary>
    public static void AtExit()
    {
        try
        {
            if (UpdateInstall.PendingHere is { } p)
            {
                if (OtherCopyOpen()) AppLog.Write("updates", $"{p.Version} waits: another {AppIdentity.ProductName} from this folder is still open.");
                else
                {
                    try
                    {
                        int n = UpdateInstall.Apply(p.Folder, p.InstallRoot);
                        AppLog.Write("updates", $"Put {p.Version} in ({n} files).");
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
                    {
                        AppLog.Error($"Putting {p.Version} in", e);
                        try { File.WriteAllText(ErrorFile, $"{AppIdentity.ProductName} {p.Version} couldn't be put in when it last closed ({e.Message}), so everything was left as it was. It's downloaded again at the next check."); }
                        catch (IOException) { }
                    }
                    UpdateInstall.ClearDownloads();
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            AppLog.Error("Updates at close", e);
        }
        if (StartAfterClose is { } exe && File.Exists(exe))
        {
            try { Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! }); }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { AppLog.Error("Starting again", e); }
        }
    }

    static bool OtherCopyOpen()
    {
        try
        {
            using var me = Process.GetCurrentProcess();
            var path = Environment.ProcessPath;
            foreach (var other in Process.GetProcessesByName(me.ProcessName))
                using (other)
                {
                    if (other.Id == me.Id) continue;
                    try { if (string.Equals(other.MainModule?.FileName, path, StringComparison.OrdinalIgnoreCase)) return true; }
                    catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { return true; }
                }
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
        return false;
    }
}
