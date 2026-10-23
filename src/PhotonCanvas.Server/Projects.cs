using System.Security.Cryptography;
using System.Text;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using SkiaSharp;

namespace PhotonCanvas.Server;

/// <summary>A design the phone can load: from the desktop app's recent files or a project folder.</summary>
public sealed record ProjectInfo(string Id, string Name, string Path, string Folder, DateTime Modified, bool Recent);

/// <summary>A design built into a job, exactly as the desktop app's Ship It builds it, with its checks.</summary>
public sealed record ProjectBuild(ProjectInfo Project, string Design, string Path, DocState State, BuiltJob Job, JobReview Review, DateTime BuiltAt, string Key)
{
    /// <summary>Something about this design that the phone can't do as the desktop app would.</summary>
    public string? PhoneNote { get; init; }
}

/// <summary>Finds designs and builds them, keeping the last few builds (a check and the load after it use one build).</summary>
public sealed class Projects(RemoteConfig config)
{
    readonly Lock _gate = new();
    readonly List<ProjectBuild> _builds = [];
    readonly SemaphoreSlim _building = new(1, 1);

    /// <summary>Recent files first (newest first), then the project folders' designs by date. At most 300.</summary>
    public List<ProjectInfo> List()
    {
        var settings = DesktopSettings.Get();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var o = new List<ProjectInfo>();
        void Add(string path, bool recent)
        {
            try
            {
                var full = System.IO.Path.GetFullPath(path);
                if (!IsDesign(full) || !File.Exists(full) || !seen.Add(full)) return;
                o.Add(new ProjectInfo(IdOf(full), System.IO.Path.GetFileNameWithoutExtension(full), full,
                    System.IO.Path.GetDirectoryName(full) ?? "", File.GetLastWriteTime(full), recent));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
        }
        if (settings.LastOpenFile is { } last) Add(last, true);
        foreach (var r in settings.RecentFiles) Add(r, true);
        var found = new List<string>();
        foreach (var folder in Folders())
            found.AddRange(Find(folder, 2));
        foreach (var f in found.OrderByDescending(SafeTime)) Add(f, false);
        return o.Count > 300 ? o[..300] : o;
    }

    /// <summary>Where to look besides the recent files: the configured folders, else Documents.</summary>
    public IEnumerable<string> Folders() => config.ProjectFolders.Count > 0
        ? config.ProjectFolders
        : [Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)];

    public ProjectInfo? Find(string id) => List().FirstOrDefault(p => p.Id == id);

    static bool IsDesign(string path) =>
        path.EndsWith(DocFile.Extension, StringComparison.OrdinalIgnoreCase) || path.EndsWith(DocFile.LegacyExtension, StringComparison.OrdinalIgnoreCase);

    /// <summary>A short id for a path that stays the same between calls (the phone never sees paths in URLs).</summary>
    public static string IdOf(string fullPath) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(fullPath.ToUpperInvariant())))[..16];

    static IEnumerable<string> Find(string folder, int depth)
    {
        var o = new List<string>();
        try
        {
            if (!Directory.Exists(folder)) return o;
            o.AddRange(Directory.EnumerateFiles(folder).Where(IsDesign));
            if (depth > 0)
                foreach (var sub in Directory.EnumerateDirectories(folder))
                {
                    var name = System.IO.Path.GetFileName(sub);
                    if (name.StartsWith('.') || name is "node_modules" or "bin" or "obj") continue;
                    o.AddRange(Find(sub, depth - 1));
                }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return o;
    }

    static DateTime SafeTime(string path)
    {
        try { return File.GetLastWriteTime(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return DateTime.MinValue; }
    }

    /// <summary>What a build depends on: the design file and the desktop app's settings, as they are now.</summary>
    static string KeyOf(ProjectInfo p) => $"{p.Path}|{SafeTime(p.Path).Ticks}|{SafeTime(DesktopSettings.FilePath).Ticks}";

    /// <summary>
    /// The design built into a job (or the last build, if neither it nor the settings changed since).
    /// Throws <see cref="RemoteException"/> when it can't be built.
    /// </summary>
    public async Task<ProjectBuild> BuildAsync(ProjectInfo p, Func<CancellationToken, Task<double?>> focusDistance, CancellationToken ct)
    {
        string key = KeyOf(p);
        lock (_gate)
            if (_builds.FirstOrDefault(b => b.Key == key) is { } cached && DateTime.Now - cached.BuiltAt < TimeSpan.FromMinutes(30))
                return cached;
        await _building.WaitAsync(ct);
        try
        {
            var settings = DesktopSettings.Get();
            if (settings.ActiveSetup().Kind != MachineKind.XTool)
                throw new RemoteException(409, "The desktop app is set to a GRBL laser. The phone only sends to the F2 Ultra.");
            DocState state;
            try { state = DocFile.Load(p.Path); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or FormatException or InvalidOperationException)
            {
                throw new RemoteException(422, $"Couldn't open \"{p.Name}\": {e.Message}");
            }
            // Dates and times as of now, as Ship It does.
            var now = DateTime.Now;
            state = VariableText.Refresh(state, now);
            var opt = JobSettings.From(settings);
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(TimeSpan.FromMinutes(3));
            BuiltJob job;
            try { job = await Task.Run(() => JobBuilder.Build(state, opt with { Cancel = limit.Token }), limit.Token); }
            catch (JobException e) { throw new RemoteException(422, e.Message); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new RemoteException(422, $"\"{p.Name}\" took too long to prepare (over 3 minutes). Send it from the desktop app.");
            }
            var review = JobChecks.Review(state, job, opt, settings, await focusDistance(ct));
            // Serial numbers and rows move on after a run in the desktop app (in the open design); the phone leaves the file alone.
            string? note = ReferenceEquals(VariableText.AfterRun(state, now), state) ? null
                : "This design has numbers or rows that move on after each run. The phone doesn't move them on: open the design in the desktop app for the next ones.";
            var build = new ProjectBuild(p, p.Name, p.Path, state, job, review, DateTime.Now, key) { PhoneNote = note };
            lock (_gate)
            {
                _builds.RemoveAll(b => b.Project.Path == p.Path);
                _builds.Insert(0, build);
                if (_builds.Count > 4) _builds.RemoveRange(4, _builds.Count - 4);
            }
            return build;
        }
        finally { _building.Release(); }
    }

    /// <summary>The last build of this design, if any (for its preview picture).</summary>
    public ProjectBuild? LastBuild(string id)
    {
        lock (_gate) return _builds.FirstOrDefault(b => b.Project.Id == id);
    }

    /// <summary>What the job will mark, drawn on white in each layer's colour: fills shaded, cuts as lines. PNG.</summary>
    public static byte[] Preview(ProjectBuild b, int width)
    {
        var bounds = b.Job.Bounds;
        width = Math.Clamp(width, 100, 2000);
        double margin = Math.Max(bounds.Width, bounds.Height) * 0.04 + 0.5;
        double w = Math.Max(1, bounds.Width + 2 * margin), h = Math.Max(1, bounds.Height + 2 * margin);
        int height = Math.Clamp((int)Math.Round(width * h / w), 50, 2000);
        float scale = (float)Math.Min(width / w, height / h);
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);
        canvas.Scale(scale);
        canvas.Translate((float)(margin - bounds.MinX), (float)(margin - bounds.MinY));
        var colours = b.State.Layers.ToDictionary(l => l.Id, l => new SKColor((byte)(l.Color >> 16), (byte)(l.Color >> 8), (byte)l.Color));
        SKColor Colour(Guid layer) => colours.TryGetValue(layer, out var c) ? c : new SKColor(0x3B, 0x82, 0xF6);
        // Pictures as themselves, in grey (the job only has their outline), as far as the job reaches.
        var pictureLayers = new HashSet<Guid>();
        canvas.Save();
        canvas.ClipRect(SKRect.Create((float)bounds.MinX, (float)bounds.MinY, (float)bounds.Width, (float)bounds.Height));
        using (var grey = new SKPaint { FilterQuality = SKFilterQuality.Medium, ColorFilter = SKColorFilter.CreateColorMatrix(
                   [0.299f, 0.587f, 0.114f, 0, 0, 0.299f, 0.587f, 0.114f, 0, 0, 0.299f, 0.587f, 0.114f, 0, 0, 0, 0, 0, 1, 0]) })
            foreach (var layer in JobBuilder.OutputLayers(b.State).Where(l => l.Mode == LayerMode.Image))
                foreach (var img in b.State.ShapesFor(layer).OfType<ImageShape>())
                {
                    using var bmp = SKBitmap.Decode(img.Source.Encoded);
                    if (bmp is null) continue;
                    pictureLayers.Add(layer.Id);
                    var t = img.Transform;
                    var m = new SKMatrix((float)t.A, (float)t.C, (float)t.E, (float)t.B, (float)t.D, (float)t.F, 0, 0, 1);
                    canvas.Save();
                    canvas.Concat(ref m);
                    canvas.DrawBitmap(bmp, SKRect.Create(0, 0, (float)img.SizeMm.X, (float)img.SizeMm.Y), grey);
                    canvas.Restore();
                }
        canvas.Restore();
        // Each layer's filled areas as one shape (even-odd, so letters keep their holes), under the lines.
        var fills = new Dictionary<Guid, SKPath>();
        var lines = new List<(Guid Layer, SKPath Path)>();
        foreach (var s in b.Job.Steps)
        {
            if (s.Points.Count < 2 || s.Fill && pictureLayers.Contains(s.LayerId)) continue;
            var pts = s.Points.Select(p => new SKPoint((float)p.X, (float)p.Y)).ToArray();
            if (s.Fill)
            {
                if (!fills.TryGetValue(s.LayerId, out var area)) fills[s.LayerId] = area = new SKPath { FillType = SKPathFillType.EvenOdd };
                area.AddPoly(pts, true);
            }
            else
            {
                var path = new SKPath();
                path.AddPoly(pts, s.Closed);
                lines.Add((s.LayerId, path));
            }
        }
        using (var fill = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = true })
            foreach (var (layer, area) in fills)
            {
                fill.Color = Colour(layer).WithAlpha(90);
                canvas.DrawPath(area, fill);
                area.Dispose();
            }
        using (var line = new SKPaint { Style = SKPaintStyle.Stroke, IsAntialias = true, StrokeWidth = 1.5f / scale, StrokeJoin = SKStrokeJoin.Round, StrokeCap = SKStrokeCap.Round })
            foreach (var (layer, path) in lines)
            {
                line.Color = Colour(layer);
                canvas.DrawPath(path, line);
                path.Dispose();
            }
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }
}
