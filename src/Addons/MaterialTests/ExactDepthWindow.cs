using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PhotonCanvas.Core;
using PhotonCanvas.Core.Camera;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Materials;
using PhotonCanvas.Device;
using static PhotonCanvas.App.UiKit;

using PhotonCanvas.App;

namespace PhotonCanvas.Addon.MaterialTests;

/// <summary>
/// Engrave to an exact depth: the surface is measured with the red pointer, the shapes are burned
/// a round of passes at a time (you ship each round as usual), and after each the pointer measures
/// how deep it has got and sets the next round from what each pass really took off.
/// </summary>
public sealed class ExactDepthWindow : Window
{
    public static string Folder => Path.Combine(AppIdentity.DataDir, "depth");

    readonly Document _doc;
    readonly MachinePanel _machine;
    readonly MaterialLibrary _lib;
    DepthRun? _run;
    Layer? _layer;
    readonly List<PathShape> _shapes;

    readonly NumericUpDown _target = Number(0.05m, 20, 0.1m, "0.##"), _guess = Number(0.005m, 2, 0.01m, "0.###"), _tol = Number(0.01m, 0.5m, 0.01m, "0.##");
    readonly PointsPreview _preview = new() { Height = 200, Margin = new(4) };
    readonly DepthChart _chart = new() { Height = 180, Margin = new(4) };
    readonly StackPanel _steps = new() { Spacing = 8 };
    readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    readonly ProgressBar _busy = new() { IsIndeterminate = true, IsVisible = false, Height = 4 };
    readonly StackPanel _setup;
    CancellationTokenSource? _cts;

    public ExactDepthWindow(Document doc, MachinePanel machine, MaterialLibrary lib, IReadOnlySet<Guid> selection)
    {
        _doc = doc;
        _machine = machine;
        _lib = lib;
        Title = "Exact depth";
        Width = 760;
        Height = 860;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");
        _target.Value = 1;
        _tol.Value = 0.05m;
        _guess.Value = 0.1m;

        // A run already going (this design's depth layer), else the shapes picked.
        _layer = doc.State.Layers.FirstOrDefault(l => l.Name.StartsWith(DepthRun.LayerPrefix, StringComparison.Ordinal) && DepthRun.Load(Folder, l.Id) is not null);
        _run = _layer is null ? null : DepthRun.Load(Folder, _layer.Id);
        _shapes = _layer is not null
            ? [.. doc.State.Shapes.OfType<PathShape>().Where(s => s.LayerId == _layer.Id)]
            : [.. doc.State.Shapes.OfType<PathShape>().Where(s => selection.Contains(s.Id) && s is not TextShape && s.Geometry.Subpaths.Any(sp => sp.Closed))];
        // A guess at the depth per pass from an etch test of these settings, if the library has one.
        var source = _shapes.Count > 0 ? doc.State.Layers.FirstOrDefault(l => l.Id == _shapes[0].LayerId) : null;
        if (source is not null && _lib.Materials.SelectMany(m => m.Recipes).Where(r => r.EtchMmPerPass is > 0)
                .OrderBy(r => Math.Abs(r.Settings.Laser.Power - source.Laser.Power) + Math.Abs(r.Settings.Laser.Speed - source.Laser.Speed) / 10).FirstOrDefault() is { } etch)
            _guess.Value = (decimal)Math.Round(etch.EtchMmPerPass!.Value, 3);
        foreach (var n in new[] { _target, _guess, _tol }) n.ValueChanged += (_, _) => Refresh();

        var start = new Button { Content = "Start: measure points chosen, a layer of its own", Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
        start.Click += (_, _) => Start();
        _setup = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Explained("How deep", "The depth to reach, and how close is close enough.",
                    Row(Labelled("Depth (mm)", _target), Labelled("Within (mm)", _tol))),
                Explained("A first guess", "How deep one pass of these settings goes (an etch-rate test gives it). Only the first round uses it; after that the measured depth decides.",
                    Labelled("About this deep a pass (mm)", _guess)),
                start,
            },
        };
        var cancel = new Button { Content = "Close", MinWidth = 100 };
        cancel.Click += (_, _) => Close();
        Closing += (_, _) => _cts?.Cancel();
        Content = new Border
        {
            Padding = new(16),
            Child = new ScrollViewer
            {
                Content = new StackPanel
                {
                    Spacing = 12,
                    Children =
                    {
                        new TextBlock
                        {
                            TextWrapping = TextWrapping.Wrap,
                            Text = "Burn the shapes a round of passes at a time. The red pointer measures the surface first, and after every round measures how deep it has got: " +
                                   "the next round is worked out from what each pass really took off, so it closes in on the depth instead of guessing all the passes up front.",
                        },
                        new Border { Background = Brush("Bg0"), CornerRadius = new(8), Padding = new(8), Child = _preview },
                        Hint("Blue dots: where the depth is measured. Grey: just outside, to catch the board or the head shifting. The pointer must see the bottom: keep holes at least about 5 mm across (it measures 2.5 mm in from every edge)."),
                        _setup,
                        _steps,
                        Explained("Depth so far", "Each dot is a measurement after a round; the line across is the depth wanted.", _chart),
                        _busy,
                        _status,
                        new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel } },
                    },
                },
            },
        };
        Refresh();
    }

    /// <summary>Is a depth job waiting for its measurement after a burn?</summary>
    public static bool Waiting(DocState s) =>
        s.Layers.Any(l => l.Name.StartsWith(DepthRun.LayerPrefix, StringComparison.Ordinal) && DepthRun.Load(Folder, l.Id) is { Next: DepthStep.MeasureDepth });

    PhotonCanvas.Doc.PathGeometry Area => new([.. _shapes.SelectMany(s => s.World.Subpaths)]);

    void Refresh()
    {
        _setup.IsVisible = _run is null;
        var run = _run;
        if (run is null && _shapes.Count > 0)
        {
            var (inside, outside) = DepthRun.PickPoints(Area);
            _preview.Show(_shapes, inside, outside);
        }
        else if (run is not null) _preview.Show(_shapes, run.Inside, run.Outside);
        else _preview.Show([], [], []);
        if (_shapes.Count == 0 && run is null)
        {
            _status.Text = "Select the shapes to engrave deep (closed outlines) and open this again.";
            return;
        }
        _chart.Show(run?.History ?? [], run?.TargetMm ?? (double)(_target.Value ?? 1));
        Steps();
    }

    void Steps()
    {
        _steps.Children.Clear();
        if (_run is not { } run) return;
        var next = run.Next;
        Control Step(int n, string title, string about, bool done, bool current, Button? action)
        {
            var mark = new TextBlock { Text = done ? "✓" : $"{n}", Width = 22, FontWeight = FontWeight.SemiBold, Foreground = done ? Brush("Ok") : current ? Brush("Accent") : Brush("TextMuted"), VerticalAlignment = VerticalAlignment.Top };
            var body = new StackPanel { Spacing = 4, Children = { new TextBlock { Text = title, FontWeight = current ? FontWeight.SemiBold : FontWeight.Normal, TextWrapping = TextWrapping.Wrap }, Hint(about) } };
            if (action is not null && current) body.Children.Add(action);
            var row = new DockPanel { Children = { mark, body } };
            DockPanel.SetDock(mark, Dock.Left);
            return new Border { Padding = new(10), CornerRadius = new(8), Background = current ? Brush("Bg2") : Brushes.Transparent, Child = row };
        }
        Button Act(string text, Func<Task> go)
        {
            var b = new Button { Content = text, Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 4, 0, 0) };
            b.Click += async (_, _) => await go();
            return b;
        }
        _steps.Children.Add(Step(1, "Measure the surface", "Before anything is burned: the pointer reads the height at every dot. Focus on the material first.",
            run.Before is not null, next == DepthStep.MeasureSurface, Act("Measure the surface", () => Measure(surface: true))));
        int k = 1;
        foreach (var r in run.Rounds)
        {
            bool measured = r.Heights is not null;
            _steps.Children.Add(Step(++k, $"Round {k - 1}: {r.Passes} pass{(r.Passes == 1 ? "" : "es")}",
                measured ? $"Burned and measured: {(run.History.FirstOrDefault(h => h.Passes == run.Rounds.Take(k - 1).Sum(x => x.Passes)).DepthMm):0.00} mm deep."
                         : "Frame it and Ship It as usual. When it's done, brush the dust and char out of the hole, then measure.",
                measured, !measured && next == DepthStep.MeasureDepth, Act("Measure the depth", () => Measure(surface: false))));
        }
        if (next == DepthStep.Burn)
        {
            int passes = run.NextPasses();
            string why = run.History.Count == 0
                ? $"A first round to find out how deep a pass really goes (guessed {run.GuessMmPerPass:0.###} mm)."
                : $"{run.DepthMm:0.00} mm so far; a pass now takes off about {run.RateMmPerPass:0.###} mm, {run.TargetMm - run.DepthMm:0.00} mm to go.";
            _steps.Children.Add(Step(++k, $"Round {run.Rounds.Count + 1}: {passes} pass{(passes == 1 ? "" : "es")}", why, false, true,
                Act($"Set up {passes} pass{(passes == 1 ? "" : "es")} and close", () => { Burn(passes); return Task.CompletedTask; })));
        }
        if (next == DepthStep.Finished)
        {
            var last = run.History[^1];
            _steps.Children.Add(Step(++k, $"Done: {run.DepthMm:0.00} mm deep", $"Target {run.TargetMm:0.00} ± {run.ToleranceMm:0.00} mm, in {run.PassesSoFar} passes over {run.Rounds.Count} rounds. " +
                $"The measured points differ by {last.Spread:0.00} mm." + (last.Spread > run.ToleranceMm * 2 ? " That's uneven: the floor isn't flat (focus, char or air flow)." : ""),
                true, true, Act("Finish (keep the shapes on their layer)", () => { Finish(); return Task.CompletedTask; })));
        }
    }

    void Start()
    {
        if (_shapes.Count == 0) return;
        var (inside, outside) = DepthRun.PickPoints(Area);
        if (inside.Count == 0) { _status.Text = "The shapes are too narrow to measure inside (the pointer measures 2.5 mm in from every edge, so holes need about 5 mm across)."; return; }
        // A layer of its own, from the shapes' layer: a fill, its passes set each round.
        var source = _doc.State.Layers.First(l => l.Id == _shapes[0].LayerId);
        double target = (double)(_target.Value ?? 1);
        var layer = source with { Id = Guid.NewGuid(), Name = $"{DepthRun.LayerPrefix}{target:0.##} mm", Mode = LayerMode.Fill, Laser = source.Laser with { Passes = 1 }, ParentId = null };
        var s = Ops.AddLayer(_doc.State, layer);
        s = Ops.MoveToLayer(s, _shapes.Select(x => x.Id).ToHashSet(), layer.Id);
        _doc.Commit("Exact depth", s, _shapes.Select(x => x.Id));
        _layer = layer;
        _run = new DepthRun
        {
            LayerId = layer.Id, TargetMm = target, ToleranceMm = (double)(_tol.Value ?? 0.05m), GuessMmPerPass = (double)(_guess.Value ?? 0.1m),
            Inside = inside, Outside = outside,
        };
        _run.Save(Folder);
        AppLog.Write("exact depth", $"Started: {target:0.##} mm, {inside.Count} points inside, guess {_run.GuessMmPerPass:0.###} mm a pass.");
        Refresh();
    }

    async Task Measure(bool surface)
    {
        if (_run is not { } run) return;
        if (_machine.Client is not { IsConnected: true } client) { _status.Text = "Connect to the machine first."; return; }
        if (SurfaceProbe.LoadCalibration() is not { } cal) { _status.Text = "Calibrate the pointer first (Work area → Curved surface → Calibrate the pointer)."; return; }
        if (_machine.Distance is not { } focus) { _status.Text = "Set the focus on the material first (auto-focus, or type its height in the machine panel)."; return; }
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        _busy.IsVisible = true;
        try
        {
            var heights = await new SurfaceProbe(client).MeasureAsync(run.Points, focus, cal, new Progress<string>(t => _status.Text = t), cts.Token);
            int missing = heights.Take(run.Inside.Count).Count(h => h is null);
            if (missing == run.Inside.Count) { _status.Text = "None of the points inside could be read: is the pointer seeing the bottom? (Brush out the char; very dark floors can hide the dot.)"; return; }
            _run = run.Measured(heights);
            _run.Save(Folder);
            _status.Text = surface ? $"Surface measured at {heights.Count(h => h is not null)} of {heights.Count} points." :
                $"{_run.DepthMm:0.00} mm deep after {_run.PassesSoFar} passes." + (missing > 0 ? $" {missing} point{(missing == 1 ? "" : "s")} inside couldn't be read." : "");
            AppLog.Write("exact depth", _status.Text);
        }
        catch (OperationCanceledException) { _status.Text = "Stopped."; }
        catch (Exception e) when (e is DeviceException or InvalidOperationException or IOException) { _status.Text = e.Message; }
        finally { _busy.IsVisible = false; Refresh(); }
    }

    void Burn(int passes)
    {
        if (_run is not { } run || _layer is null) return;
        _doc.Commit($"Exact depth: {passes} passes", Ops.UpdateLayers(_doc.State, new HashSet<Guid> { _layer.Id }, l => l with { Laser = l.Laser with { Passes = passes } }));
        _run = run.Burning(passes);
        _run.Save(Folder);
        AppLog.Write("exact depth", $"Round {_run.Rounds.Count}: {passes} passes set.");
        // Out of the way so it can be framed and shipped; it opens again when the job is done.
        Close();
    }

    void Finish()
    {
        if (_layer is null) return;
        try { File.Delete(DepthRun.FileFor(Folder, _layer.Id)); } catch (IOException) { }
        Close();
    }

    /// <summary>The shapes from above, with the measuring points.</summary>
    sealed class PointsPreview : Control
    {
        List<PathShape> _shapes = [];
        IReadOnlyList<Vec2> _in = [], _out = [];

        public void Show(List<PathShape> shapes, IReadOnlyList<Vec2> inside, IReadOnlyList<Vec2> outside)
        {
            (_shapes, _in, _out) = (shapes, inside, outside);
            InvalidateVisual();
        }

        public override void Render(DrawingContext ctx)
        {
            var box = _shapes.Select(s => s.Bounds).Concat(_out.Select(p => new Rect2(p.X, p.Y, p.X, p.Y))).Aggregate(Rect2.Empty, (a, b) => a.Union(b));
            if (box.IsEmpty) return;
            box = box.Inflate(2);
            double k = Math.Min(Bounds.Width / box.Width, Bounds.Height / box.Height);
            double ox = (Bounds.Width - box.Width * k) / 2, oy = (Bounds.Height - box.Height * k) / 2;
            Point P(Vec2 v) => new(ox + (v.X - box.MinX) * k, oy + (v.Y - box.MinY) * k);
            var fill = new SolidColorBrush(Color.Parse("#7A5232"));
            foreach (var s in _shapes)
            {
                var geo = new StreamGeometry();
                using (var g = geo.Open())
                {
                    g.SetFillRule(FillRule.EvenOdd);
                    foreach (var f in s.World.Flatten(0.1).Where(f => f.Closed && f.Points.Count > 2))
                    {
                        g.BeginFigure(P(f.Points[0]), true);
                        foreach (var p in f.Points.Skip(1)) g.LineTo(P(p));
                        g.EndFigure(true);
                    }
                }
                ctx.DrawGeometry(fill, new Pen(new SolidColorBrush(Color.Parse("#3A2416")), 1), geo);
            }
            foreach (var p in _in) ctx.DrawEllipse(Brush("Accent"), new Pen(Brushes.White, 1.5), P(p), 6, 6);
            foreach (var p in _out) ctx.DrawEllipse(new SolidColorBrush(Color.Parse("#9AA0A6")), new Pen(Brushes.White, 1.5), P(p), 5, 5);
        }
    }

    /// <summary>Depth after each round against passes, with the target.</summary>
    sealed class DepthChart : Control
    {
        IReadOnlyList<(int Passes, double DepthMm, double Spread)> _history = [];
        double _target = 1;

        public void Show(IReadOnlyList<(int Passes, double DepthMm, double Spread)> history, double target)
        {
            (_history, _target) = (history, target);
            InvalidateVisual();
        }

        public override void Render(DrawingContext ctx)
        {
            var text = ((ISolidColorBrush)Brush("TextMain")).Color;
            var muted = new SolidColorBrush(Color.FromArgb(170, text.R, text.G, text.B));
            double l = 50, t = 10, r = Bounds.Width - 12, b = Bounds.Height - 28;
            if (r <= l + 20 || b <= t + 20) return;
            int maxPasses = Math.Max(4, _history.Count > 0 ? _history[^1].Passes * 5 / 4 : 10);
            double maxDepth = Math.Max(_target * 1.15, _history.Count > 0 ? _history.Max(h => h.DepthMm) * 1.1 : 0);
            Point P(double passes, double depth) => new(l + (r - l) * passes / maxPasses, t + (b - t) * depth / maxDepth);
            void Write(string s, Point at, bool right = false)
            {
                var f = new FormattedText(s, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Inter"), 11, muted);
                ctx.DrawText(f, new Point(at.X - (right ? f.Width + 4 : f.Width / 2), at.Y - f.Height / 2));
            }
            var grid = new Pen(new SolidColorBrush(Color.FromArgb(40, text.R, text.G, text.B)), 1);
            for (int i = 0; i <= 4; i++)
            {
                double d = maxDepth * i / 4;
                ctx.DrawLine(grid, P(0, d), P(maxPasses, d));
                Write($"{d:0.0} mm", P(0, d), right: true);
            }
            Write("passes →", new Point((l + r) / 2, Bounds.Height - 8));
            Write("0", P(0, maxDepth) + new Point(0, 12));
            Write($"{maxPasses}", P(maxPasses, maxDepth) + new Point(-6, 12));
            // Depth goes down the chart, like the hole.
            ctx.DrawLine(new Pen(Brush("Ok"), 1.5, new DashStyle([5, 4], 0)), P(0, _target), P(maxPasses, _target));
            Write($"{_target:0.##} mm wanted", P(maxPasses, _target) + new Point(-50, -10));
            var accent = Brush("Accent");
            var prev = P(0, 0);
            foreach (var (passes, depth, _) in _history)
            {
                var at = P(passes, depth);
                ctx.DrawLine(new Pen(accent, 2), prev, at);
                ctx.DrawEllipse(accent, null, at, 4.5, 4.5);
                prev = at;
            }
        }
    }
}
