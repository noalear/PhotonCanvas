using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Clipper2Lib;
using PhotonCanvas.Addon.Furniture;
using PhotonCanvas.App;
using Geo = PhotonCanvas.Addon.Walls.Geo;

namespace PhotonCanvas.Addon.Buildings;

/// <summary>What the 3D view shows of the building.</summary>
public enum ViewMode { Whole, CutAway, Storey }

/// <summary>
/// The building put together, drawn in 3D from above and to one side: every piece at its thickness where
/// it sits, the etched faces showing their texture. Drag to turn it round. It can cut the front and
/// right-hand walls (and the roof) away to show the rooms, or show one storey on its own.
/// </summary>
public sealed class BuildingView : Control
{
    /// <summary>A flat face of one piece: its outlines (holes included), facing <see cref="Normal"/>.</summary>
    sealed record Face(List<V3[]> Loops, V3 Normal, double Shade, PathsD? Etched, List<PathD>? Lines, Pose? Pose, double W, bool Wood)
    {
        /// <summary>The roof's outside: nothing of the roof is in front of it, so it's drawn as near as its nearest corner.</summary>
        public bool Outside { get; init; }
    }

    readonly List<Face> _faces = [];
    BuildingKit? _kit;
    V3 _centre;
    double _yaw = 32, _pitch = 26;
    Point? _drag;
    string? _message;

    public ViewMode Mode { get; private set; } = ViewMode.Whole;
    public int Storey { get; private set; }

    static readonly Color Wood = Color.FromRgb(0xDD, 0xBE, 0x92), Stone = Color.FromRgb(0xD2, 0xC6, 0xB4);
    static readonly IBrush Burnt = new SolidColorBrush(Color.FromRgb(0x7E, 0x5A, 0x38));
    static readonly IBrush Score = new SolidColorBrush(Color.FromRgb(0x6B, 0x47, 0x28));
    static readonly IBrush Edge = new SolidColorBrush(Color.FromRgb(0x4A, 0x33, 0x1E));

    public BuildingView()
    {
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.SizeAll);
    }

    /// <summary>Shows a building (or, with none, a message saying why there's nothing to show).</summary>
    public void Show(BuildingKit? kit, string? message = null)
    {
        _kit = kit;
        _message = message;
        Build();
    }

    public void SetMode(ViewMode mode, int storey = 0)
    {
        Mode = mode;
        Storey = storey;
        Build();
    }

    bool Shown(Solid s) => Mode switch
    {
        ViewMode.CutAway => s.Side is not (WallSide.Front or WallSide.Right or WallSide.Roof),
        ViewMode.Storey => s.Storey == Storey && s.Side is not (WallSide.Front or WallSide.Right or WallSide.Roof),
        // Inside the roof, out of sight: left out, as they'd show through the slopes in a painter's drawing.
        _ => !(s.Side == WallSide.Roof && (s.Name.StartsWith("Roof truss", StringComparison.Ordinal) || s.Name.StartsWith("Roof locator", StringComparison.Ordinal))),
    };

    void Build()
    {
        _faces.Clear();
        if (_kit is { } kit)
        {
            var lo = new V3(double.MaxValue, double.MaxValue, double.MaxValue);
            var hi = new V3(double.MinValue, double.MinValue, double.MinValue);
            // The whole building's middle, whatever is shown, so it stays put when the view changes.
            foreach (var s in kit.Solids)
            {
                var b = Geo.Bounds(s.Outline);
                foreach (var u in new[] { b.left, b.right })
                    foreach (var v in new[] { b.top, b.bottom })
                    {
                        var q = s.Pose.At(u, v);
                        lo = new V3(Math.Min(lo.X, q.X), Math.Min(lo.Y, q.Y), Math.Min(lo.Z, q.Z));
                        hi = new V3(Math.Max(hi.X, q.X), Math.Max(hi.Y, q.Y), Math.Max(hi.Z, q.Z));
                    }
            }
            _centre = (lo + hi) * 0.5;
            var shown = kit.Solids.Where(Shown).ToList();
            foreach (var s in shown) Add(s, sides: s.Side != WallSide.Ground);
            // The floor tiles lie edge to edge: their sides are drawn only round the outside of them all,
            // or the sides between them would show through the tiles' tops.
            if (shown.FirstOrDefault(s => s.Side == WallSide.Ground) is { } first)
            {
                var all = new PathsD();
                foreach (var t in shown.Where(s => s.Side == WallSide.Ground))
                {
                    var d = t.Pose.O - first.Pose.O;
                    all.AddRange(Geo.Move(t.Outline, d.Dot(first.Pose.U), d.Dot(first.Pose.V)));
                }
                // Grown and shrunk back, to close the fit gaps between the jigsaw joins.
                Add(first with { Outline = Geo.Offset(Geo.Offset(Geo.Union(all), 0.4), -0.4) }, faces: false);
            }
        }
        InvalidateVisual();
    }

    void Add(Solid s, bool faces = true, bool sides = true)
    {
        double T = s.Thickness;
        var pose = s.Pose;
        bool wood = s.IsFurniture || s.Side is WallSide.Floor or WallSide.Roof;
        var outline = Geo.Tidy(Clipper.SimplifyPaths(s.Outline, 0.05));
        V3 At(PointD p, double w) => pose.At(p.x, p.y, w);
        var n = pose.N;
        if (faces)
        {
            _faces.Add(new Face([.. outline.Select(c => c.Select(p => At(p, T / 2)).ToArray())], n, 1, s.Etched, s.Lines, pose, T / 2, wood) { Outside = s.Side == WallSide.Roof && s.Etched is not null });
            _faces.Add(new Face([.. outline.Select(c => c.Select(p => At(p, -T / 2)).ToArray())], -n, 1, null, null, null, 0, wood));
        }
        if (!sides) return;
        foreach (var c in outline)
            for (int i = 0; i < c.Count; i++)
            {
                var a = c[i];
                var b = c[(i + 1) % c.Count];
                double dx = b.x - a.x, dy = b.y - a.y, len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 1e-6) continue;
                // Outward: to the right of the edge, as outlines run anticlockwise and holes clockwise.
                var side = pose.U * (dy / len) + pose.V * (-dx / len);
                _faces.Add(new Face([[At(a, T / 2), At(b, T / 2), At(b, -T / 2), At(a, -T / 2)]], side, 0.86, null, null, null, 0, wood));
            }
    }

    (V3 Right, V3 Up, V3 Toward) View()
    {
        double y = _yaw * Math.PI / 180, p = _pitch * Math.PI / 180;
        var d = new V3(Math.Sin(y) * Math.Cos(p), Math.Sin(p), Math.Cos(y) * Math.Cos(p));
        var r = new V3(Math.Cos(y), 0, -Math.Sin(y));
        return (r, d.Cross(r), d);
    }

    public override void Render(DrawingContext ctx)
    {
        ctx.FillRectangle(UiKit.Brush("Bg2"), new Rect(Bounds.Size));
        if (_faces.Count == 0)
        {
            if (_message is { } m) Message(ctx, Bounds, m);
            return;
        }
        var (r, u, d) = View();
        Point Flat(V3 p) { var q = p - _centre; return new Point(q.Dot(r), -q.Dot(u)); }
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        foreach (var f in _faces)
            foreach (var l in f.Loops)
                foreach (var q in l)
                {
                    var s = Flat(q);
                    x0 = Math.Min(x0, s.X); x1 = Math.Max(x1, s.X); y0 = Math.Min(y0, s.Y); y1 = Math.Max(y1, s.Y);
                }
        double k = Math.Min((Bounds.Width - 30) / Math.Max(1e-6, x1 - x0), (Bounds.Height - 40) / Math.Max(1e-6, y1 - y0));
        if (!(k > 0)) return;
        double ox = Bounds.Width / 2 - (x0 + x1) / 2 * k, oy = Bounds.Height / 2 - (y0 + y1) / 2 * k;
        Point Screen(V3 p) { var s = Flat(p); return new Point(ox + s.X * k, oy + s.Y * k); }

        var light = new V3(0.35, 0.85, 0.45);
        light *= 1 / light.Length;
        var edge = new Pen(Edge, 0.6);
        var score = new Pen(Score, Math.Clamp(k * 0.05, 0.25, 0.8));
        // While turning it round, the etching is left off so it keeps up.
        bool detail = _drag is null;
        var seen = _faces.Where(f => f.Normal.Dot(d) > 1e-6)
                         .Select(f => (Face: f, Depth: f.Outside ? f.Loops[0].Max(q => (q - _centre).Dot(d)) : f.Loops[0].Average(q => (q - _centre).Dot(d))))
                         .OrderBy(x => x.Depth);
        foreach (var (f, _) in seen)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.SetFillRule(Avalonia.Media.FillRule.EvenOdd);
                foreach (var l in f.Loops)
                {
                    c.BeginFigure(Screen(l[0]), true);
                    for (int i = 1; i < l.Length; i++) c.LineTo(Screen(l[i]));
                    c.EndFigure(true);
                }
            }
            double lit = (0.62 + 0.38 * Math.Max(0, f.Normal.Dot(light))) * f.Shade;
            var col = f.Wood ? Wood : Stone;
            ctx.DrawGeometry(new SolidColorBrush(Color.FromRgb((byte)(col.R * lit), (byte)(col.G * lit), (byte)(col.B * lit))), edge, g);
            if (detail && f.Pose is { } pose)
            {
                Point On(PointD p) => Screen(pose.At(p.x, p.y, f.W));
                if (f.Etched is { } et) ctx.DrawGeometry(Burnt, null, Shape(et, On, true));
                if (f.Lines is { } ln) ctx.DrawGeometry(null, score, Shape(ln, On, false));
            }
        }
        var hint = new FormattedText("Drag to turn it round", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default), 11, UiKit.Brush("TextMuted"));
        ctx.DrawText(hint, new Point(10, Bounds.Height - hint.Height - 6));
    }

    internal static void Message(DrawingContext ctx, Rect bounds, string text)
    {
        var ft = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default), 13, UiKit.Brush("TextMuted"))
        {
            MaxTextWidth = Math.Max(50, bounds.Width - 40),
            TextAlignment = TextAlignment.Center,
        };
        ctx.DrawText(ft, new Point((bounds.Width - ft.Width) / 2, (bounds.Height - ft.Height) / 2));
    }

    internal static StreamGeometry Shape(IEnumerable<PathD> paths, Func<PointD, Point> on, bool closed)
    {
        var g = new StreamGeometry();
        using var c = g.Open();
        c.SetFillRule(Avalonia.Media.FillRule.EvenOdd);
        foreach (var p in paths)
        {
            if (p.Count < 2) continue;
            c.BeginFigure(on(p[0]), closed);
            for (int i = 1; i < p.Count; i++) c.LineTo(on(p[i]));
            c.EndFigure(closed);
        }
        return g;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _drag = e.GetPosition(this);
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_drag is not { } from) return;
        var at = e.GetPosition(this);
        _yaw = (_yaw - (at.X - from.X) * 0.5) % 360;
        _pitch = Math.Clamp(_pitch + (at.Y - from.Y) * 0.4, -15, 85);
        _drag = at;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _drag = null;
        e.Pointer.Capture(null);
        InvalidateVisual();
    }
}
