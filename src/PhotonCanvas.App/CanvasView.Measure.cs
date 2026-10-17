using System.Globalization;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Geometry;
using PathGeometry = PhotonCanvas.Doc.PathGeometry;

namespace PhotonCanvas.App;

// Measure, trim and two-point align: tools that work point by point on the canvas.
public sealed partial class CanvasView
{
    static readonly IBrush ToolInk = new SolidColorBrush(Color.Parse("#FFB547"));
    static readonly IBrush LabelBack = new SolidColorBrush(Color.FromArgb(220, 20, 22, 28));
    static readonly IPen TrimPen = new Pen(new SolidColorBrush(Color.Parse("#FFB547")), 4);

    // measure
    Vec2? _measureA, _measureB;
    bool _measuring;

    // trim
    (Guid Shape, PathGeometry Result, List<Vec2> Removed)? _trim;

    // two-point align: the clicked points, source pair then target pair
    readonly List<Vec2> _align = [];

    /// <summary>A shape's corner or end near <paramref name="w"/> (within snapping distance), else the usual snapping.</summary>
    Vec2 SnapVertex(Vec2 w)
    {
        if (_doc is null || SnapSuspended) return w;
        double tol = SnapDistancePx / _scale, best = tol;
        Vec2? hit = null;
        var visible = _doc.State.Layers.Where(l => l.Visible).Select(l => l.Id).ToHashSet();
        foreach (var s in _doc.State.Shapes)
        {
            if (s is not PathShape ps || !visible.Contains(s.LayerId) || !ps.Bounds.Inflate(tol).Contains(w)) continue;
            foreach (var sp in ps.World.Subpaths)
            {
                void Try(Vec2 p)
                {
                    double d = p.Dist(w);
                    if (d < best) { best = d; hit = p; }
                }
                Try(sp.Start);
                foreach (var seg in sp.Segs) Try(seg.To);
            }
        }
        if (hit is { } h) return h;
        PrepareSnap(new HashSet<Guid>());
        var snapped = SnapPoint(w);
        _guides.Clear();
        return snapped;
    }

    void ResetPointTools()
    {
        _measureA = _measureB = null;
        _measuring = false;
        _trim = null;
        _align.Clear();
    }

    // ---- input ----------------------------------------------------------------------

    void MeasurePressed()
    {
        _measureA = _measureB = SnapVertex(_pressWorld);
        _measuring = true;
        InvalidateVisual();
    }

    void MeasureMoved(Vec2 w)
    {
        if (!_measuring) return;
        var b = SnapVertex(w);
        if (_lastMods.HasFlag(KeyModifiers.Shift) && _measureA is { } a)
        {
            var d = b - a;
            double ang = Math.Round(Math.Atan2(d.Y, d.X) / (Math.PI / 4)) * (Math.PI / 4), len = d.Length;
            b = a + new Vec2(Math.Cos(ang) * len, Math.Sin(ang) * len);
        }
        _measureB = b;
        InvalidateVisual();
    }

    void MeasureReleased()
    {
        _measuring = false;
        if (_measureA is { } a && _measureB is { } b)
            Hint?.Invoke($"Measured {MeasureText(a, b)}. Drag again to measure something else; Esc to stop.");
    }

    static string MeasureText(Vec2 a, Vec2 b)
    {
        var d = b - a;
        double ang = Math.Atan2(-d.Y, d.X) * 180 / Math.PI;
        return string.Create(CultureInfo.InvariantCulture, $"{d.Length:0.###} mm  (Δx {d.X:0.###}, Δy {d.Y:0.###}, {ang:0.#}°)");
    }

    /// <summary>The path nearest the cursor, if it's within reach and can be trimmed.</summary>
    PathShape? TrimTarget(Point screen)
    {
        if (HitShape(screen) is PathShape ps && ps is not (TextShape or CodeShape)) return ps;
        return null;
    }

    void TrimHover(Point screen)
    {
        _trim = null;
        if (_doc is null || TrimTarget(screen) is not { } target) { InvalidateVisual(); return; }
        var w = ToWorld(screen);
        var visible = _doc.State.Layers.Where(l => l.Visible).Select(l => l.Id).ToHashSet();
        var cutters = _doc.State.Shapes.OfType<PathShape>()
            .Where(s => s.Id != target.Id && visible.Contains(s.LayerId) && s.Bounds.Intersects(target.Bounds))
            .SelectMany(s => PathTools.Polylines(s.World)).ToList();
        if (PathTools.Trim(target.World, w, cutters) is { } r) _trim = (target.Id, r.Result, r.Removed);
        InvalidateVisual();
    }

    void TrimPressed(Point screen)
    {
        TrimHover(screen);
        if (_doc is null) return;
        if (_trim is not { } t)
        {
            Hint?.Invoke("Nothing to trim there: click a piece of a line between two places where it crosses other lines.");
            return;
        }
        var shape = (PathShape)_doc.State.FindShape(t.Shape)!;
        var next = t.Result.Subpaths.All(s => s.Segs.IsEmpty)
            ? Ops.Delete(_doc.State, new HashSet<Guid> { shape.Id })
            : Ops.Replace(_doc.State, new PathShape { Id = shape.Id, Name = shape.Name, LayerId = shape.LayerId, GroupId = shape.GroupId, Geometry = t.Result });
        _doc.Commit("Trim", next);
        _trim = null;
        TrimHover(screen);
    }

    void AlignPressed()
    {
        if (_doc is null) return;
        if (_doc.Selection.Count == 0)
        {
            Hint?.Invoke("Select what to move first, then pick two points on it and the two points they should go to.");
            return;
        }
        _align.Add(SnapVertex(_pressWorld));
        if (_align.Count < 4)
        {
            Hint?.Invoke(_align.Count switch
            {
                1 => "Now the second point on the selection.",
                2 => "Now where the first point should go.",
                _ => "And where the second point should go (hold Shift to also resize, so both land exactly).",
            });
            InvalidateVisual();
            return;
        }
        var (s1, s2, t1, t2) = (_align[0], _align[1], _align[2], _align[3]);
        _align.Clear();
        var ds = s2 - s1;
        var dt = t2 - t1;
        if (ds.Length < 1e-6 || dt.Length < 1e-6) { Hint?.Invoke("The two points must be apart."); return; }
        double turn = (Math.Atan2(dt.Y, dt.X) - Math.Atan2(ds.Y, ds.X)) * 180 / Math.PI;
        double k = _lastMods.HasFlag(KeyModifiers.Shift) ? dt.Length / ds.Length : 1;
        var move = Affine.Translate(-s1.X, -s1.Y).Then(Affine.Scale(k, k)).Then(Affine.Rotate(turn)).Then(Affine.Translate(t1));
        _doc.Commit("Align by two points", Ops.Transform(_doc.State, _doc.Selection, move));
        Hint?.Invoke(string.Create(CultureInfo.InvariantCulture, $"Moved and turned {turn:0.##}°{(k != 1 ? $", resized {k * 100:0.##}%" : "")}. Pick two more points to go again, or Esc."));
        InvalidateVisual();
    }

    // ---- drawing (screen space) ----------------------------------------------------------

    void DrawPointTools(DrawingContext ctx)
    {
        if (_tool == Tool.Measure && _measureA is { } a && _measureB is { } b)
        {
            var pa = ToScreen(a);
            var pb = ToScreen(b);
            var pen = new Pen(ToolInk, 1.5);
            ctx.DrawLine(pen, pa, pb);
            Dot(ctx, pa);
            Dot(ctx, pb);
            Label(ctx, MeasureText(a, b), new Point((pa.X + pb.X) / 2 + 8, (pa.Y + pb.Y) / 2 + 8));
        }
        if (_tool == Tool.Trim && _trim is { } t && t.Removed.Count >= 2)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(ToScreen(t.Removed[0]), false);
                foreach (var p in t.Removed.Skip(1)) c.LineTo(ToScreen(p));
                c.EndFigure(false);
            }
            ctx.DrawGeometry(null, TrimPen, g);
        }
        if (_tool == Tool.Align2)
        {
            string[] names = ["1", "2", "1→", "2→"];
            for (int i = 0; i < _align.Count; i++)
            {
                var p = ToScreen(_align[i]);
                Dot(ctx, p);
                Label(ctx, names[i], new Point(p.X + 6, p.Y - 20));
            }
            if (_align.Count == 2) ctx.DrawLine(new Pen(ToolInk, 1, new DashStyle([4, 3], 0)), ToScreen(_align[0]), ToScreen(_align[1]));
            if (_align.Count == 3 && _cursor is { } cur) ctx.DrawLine(new Pen(ToolInk, 1, new DashStyle([4, 3], 0)), ToScreen(_align[2]), ToScreen(cur));
        }
    }

    Rect2? _section;

    /// <summary>The part of the design being marked when marking in sections (null: none).</summary>
    public Rect2? SectionArea
    {
        get => _section;
        set { _section = value; InvalidateVisual(); }
    }

    void DrawSection(DrawingContext ctx)
    {
        if (_section is not { } s) return;
        var r = ToScreen(s);
        ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(24, 255, 181, 71)), new Pen(ToolInk, 2, new DashStyle([6, 4], 0)), r);
        Label(ctx, "Marking this section", new Point(r.Left + 6, r.Top + 6));
    }

    static void Dot(DrawingContext ctx, Point p) => ctx.DrawEllipse(ToolInk, new Pen(Brushes.Black, 1), p, 4, 4);

    static void Label(DrawingContext ctx, string text, Point at)
    {
        var ft = Text(text, 12, Brushes.White);
        var r = new Rect(at.X - 4, at.Y - 2, ft.Width + 8, ft.Height + 4);
        ctx.FillRectangle(LabelBack, r, 3);
        ctx.DrawText(ft, at);
    }
}
