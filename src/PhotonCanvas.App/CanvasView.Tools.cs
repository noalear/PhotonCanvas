using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PathGeometry = PhotonCanvas.Doc.PathGeometry;

namespace PhotonCanvas.App;

/// <summary>Pen tool and point (node) editing.</summary>
public sealed partial class CanvasView
{
    // ---- pen ----------------------------------------------------------------------------

    /// <summary>A pen point: where it is and its curve handles (equal to the point = sharp).</summary>
    sealed record PenNode(Vec2 At, Vec2 In, Vec2 Out);

    readonly List<PenNode> _pen = [];

    const double PenSnapPx = 9;

    void PenPressed(PointerPressedEventArgs e)
    {
        PrepareSnap(new HashSet<Guid>());
        var w = SnapPoint(_pressWorld);
        _guides.Clear();
        // Click the first point to close the shape.
        if (_pen.Count >= 2 && Dist(ToScreen(_pen[0].At), _pressScreen) <= PenSnapPx)
        {
            FinishPen(closed: true);
            return;
        }
        // Double-click finishes an open path (the first click of it already added the point).
        if (e.ClickCount >= 2 && _pen.Count >= 2)
        {
            FinishPen(closed: false);
            return;
        }
        _pen.Add(new PenNode(w, w, w));
        _drag = Drag.PenHandle;
        InvalidateVisual();
    }

    void PenMoved(Vec2 w)
    {
        if (_pen.Count == 0) return;
        var last = _pen[^1];
        // Dragging pulls out a smooth curve handle; the opposite one mirrors it.
        if (Dist(ToScreen(w), ToScreen(last.At)) < 3) _pen[^1] = last with { In = last.At, Out = last.At };
        else _pen[^1] = last with { Out = w, In = last.At * 2 - w };
        InvalidateVisual();
    }

    bool PenKey(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter when _pen.Count >= 2:
                FinishPen(closed: false);
                return true;
            case Key.Escape when _pen.Count > 0:
                _pen.Clear();
                InvalidateVisual();
                return true;
            case Key.Back or Key.Delete when _pen.Count > 0:
                _pen.RemoveAt(_pen.Count - 1);
                InvalidateVisual();
                return true;
        }
        return false;
    }

    static PathGeometry PenGeometry(IReadOnlyList<PenNode> nodes, bool closed)
    {
        var segs = System.Collections.Immutable.ImmutableArray.CreateBuilder<Seg>();
        Seg Join(PenNode a, PenNode b) =>
            a.Out == a.At && b.In == b.At ? Seg.Line(b.At) : Seg.Cubic(a.Out, b.In, b.At);
        for (int i = 1; i < nodes.Count; i++) segs.Add(Join(nodes[i - 1], nodes[i]));
        if (closed) segs.Add(Join(nodes[^1], nodes[0]));
        return new PathGeometry([new Subpath(nodes[0].At, segs.ToImmutable(), closed)]);
    }

    void FinishPen(bool closed)
    {
        if (_doc is null || _pen.Count < 2) { _pen.Clear(); InvalidateVisual(); return; }
        var layer = DrawLayer();
        if (layer is not null)
        {
            var shape = new PathShape { Name = closed ? "Shape" : "Path", LayerId = layer.Id, Geometry = PenGeometry(_pen, closed) };
            _doc.Commit("Draw with pen", Ops.AddShapes(_doc.State, [shape]), [shape.Id]);
        }
        _pen.Clear();
        _drag = Drag.None;
        InvalidateVisual();
    }

    void DrawPen(DrawingContext ctx)
    {
        if (_pen.Count == 0) return;
        double px = 1 / _scale;
        var pen = new Pen(new SolidColorBrush(AccentColor), 1.5 * px);
        if (_pen.Count >= 2) ctx.DrawGeometry(null, pen, ToStream(PenGeometry(_pen, false)));
        // Rubber band to the cursor.
        if (_cursor is { } c && _drag == Drag.None)
            ctx.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(150, AccentColor.R, AccentColor.G, AccentColor.B)), px, new DashStyle([4, 3], 0)),
                ToStream(PenGeometry([_pen[^1], new PenNode(c, c, c)], false)));
        var handlePen = new Pen(new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)), px);
        foreach (var n in _pen)
        {
            if (n.Out != n.At)
            {
                ctx.DrawLine(handlePen, P(n.In), P(n.Out));
                ctx.DrawEllipse(Brushes.White, null, P(n.Out), 2.5 * px, 2.5 * px);
                ctx.DrawEllipse(Brushes.White, null, P(n.In), 2.5 * px, 2.5 * px);
            }
            double r = (n == _pen[0] && _pen.Count >= 2 ? 5 : 3.5) * px;
            ctx.DrawRectangle(Brushes.White, new Pen(new SolidColorBrush(AccentColor), 1.2 * px), new Rect(n.At.X - r, n.At.Y - r, 2 * r, 2 * r));
        }
    }

    static StreamGeometry ToStream(PathGeometry g)
    {
        var sg = new StreamGeometry();
        using var c = sg.Open();
        foreach (var sp in g.Subpaths)
        {
            c.BeginFigure(P(sp.Start), false);
            foreach (var s in sp.Segs)
            {
                if (s.Kind == SegKind.Line) c.LineTo(P(s.To));
                else c.CubicBezierTo(P(s.C1), P(s.C2), P(s.To));
            }
            c.EndFigure(sp.Closed);
        }
        return sg;
    }

    /// <summary>Layer new drawings go to: the active one, or the first visible unlocked one.</summary>
    Layer? DrawLayer()
    {
        if (_doc is null) return null;
        var lay = _doc.State.FindLayer(_doc.ActiveLayer);
        if (lay is { Visible: true, Locked: false } && lay.Mode != LayerMode.Image) return lay;
        return _doc.State.Layers.FirstOrDefault(l => l.Visible && !l.Locked && l.Mode != LayerMode.Image)
            ?? _doc.State.Layers.FirstOrDefault(l => l.Visible && !l.Locked);
    }

    // ---- point editing ------------------------------------------------------------------

    NodeRef? _node;          // selected point
    HandleSide _dragHandle;
    bool _mirrorHandle;

    /// <summary>The path being point-edited (the single selected, editable, non-text path).</summary>
    PathShape? NodeTarget()
    {
        if (_doc is null || _doc.Selection.Count != 1) return null;
        var s = _doc.State.FindShape(_doc.Selection.First());
        if (s is not PathShape ps || s is TextShape) return null;
        var layer = _doc.State.FindLayer(s.LayerId);
        return layer is { Visible: true, Locked: false } ? ps : null;
    }

    Vec2 LocalOf(PathShape s, Vec2 world) => s.Transform.IsInvertible ? s.Transform.Invert().Apply(world) : world;

    static PathShape WithGeometry(PathShape s, PathGeometry g) => new()
    {
        Id = s.Id, Name = s.Name, LayerId = s.LayerId, GroupId = s.GroupId, Transform = s.Transform, Geometry = g,
    };

    void NodesPressed(PointerPressedEventArgs e, Point p)
    {
        if (_doc is null) return;
        var target = NodeTarget();
        if (target is not null)
        {
            var g = target.Geometry;
            var t = target.Transform;
            // Handles of the selected point first, then any point.
            if (_node is { } sel && sel.Sub < g.Subpaths.Length && sel.Index < NodeEdit.AnchorCount(g.Subpaths[sel.Sub]))
            {
                var (hin, hout) = NodeEdit.Handles(g, sel);
                foreach (var (h, side) in new[] { (hin, HandleSide.In), (hout, HandleSide.Out) })
                    if (h is { } hv && Dist(ToScreen(t.Apply(hv)), p) <= 7)
                    {
                        _dragHandle = side;
                        _mirrorHandle = NodeEdit.IsSmooth(g, sel) && !e.KeyModifiers.HasFlag(KeyModifiers.Alt);
                        _drag = Drag.NodeHandle;
                        _base = _doc.State;
                        return;
                    }
            }
            foreach (var n in NodeEdit.Nodes(g))
                if (Dist(ToScreen(t.Apply(NodeEdit.Anchor(g, n))), p) <= 7)
                {
                    _node = n;
                    _drag = Drag.NodeAnchor;
                    _base = _doc.State;
                    PrepareSnap(new HashSet<Guid> { target.Id });
                    InvalidateVisual();
                    return;
                }
            // Double-click on an edge adds a point there.
            if (e.ClickCount >= 2 && NodeEdit.NearestOnSegment(g, LocalOf(target, _pressWorld)) is { } near
                && Dist(ToScreen(t.Apply(PointOn(g, near.Sub, near.Seg, near.T))), p) <= 8)
            {
                var ng = NodeEdit.Insert(g, near.Sub, near.Seg, near.T);
                _doc.Commit("Add point", Ops.Replace(_doc.State, WithGeometry(target, ng)), [target.Id]);
                _node = new NodeRef(near.Sub, near.Seg + 1);
                return;
            }
        }
        // Clicked elsewhere: pick another path to edit, or nothing.
        _node = null;
        var hit = HitShape(p);
        _doc.Select(hit is null ? [] : [hit.Id]);
        InvalidateVisual();
    }

    static Vec2 PointOn(PathGeometry g, int sub, int seg, double t)
    {
        var sp = g.Subpaths[sub];
        var s = sp.Segs[seg];
        var p0 = seg == 0 ? sp.Start : sp.Segs[seg - 1].To;
        if (s.Kind == SegKind.Line) return Vec2.Lerp(p0, s.To, t);
        double mt = 1 - t;
        return p0 * (mt * mt * mt) + s.C1 * (3 * t * mt * mt) + s.C2 * (3 * t * t * mt) + s.To * (t * t * t);
    }

    void NodesMoved(Point p, Vec2 w)
    {
        if (_doc is null || _base is null || _node is not { } n || !StartEditIfMoved(p)) return;
        if (_base.FindShape(_doc.Selection.FirstOrDefault()) is not PathShape target) return;
        var local = LocalOf(target, _drag == Drag.NodeAnchor ? SnapPoint(w) : w);
        var g = _drag == Drag.NodeAnchor
            ? NodeEdit.MoveAnchor(target.Geometry, n, local)
            : NodeEdit.MoveHandle(target.Geometry, n, _dragHandle, local, _mirrorHandle);
        _doc.UpdateEdit(Ops.Replace(_base, WithGeometry(target, g)));
    }

    bool NodesKey(KeyEventArgs e)
    {
        if (_doc is null) return false;
        var target = NodeTarget();
        switch (e.Key)
        {
            case Key.Escape:
                _node = null;
                Tool = Tool.Select;
                return true;
            case Key.Delete or Key.Back when target is not null && _node is { } n:
            {
                var g = NodeEdit.Delete(target.Geometry, n);
                var next = g.IsEmpty ? Ops.Delete(_doc.State, new HashSet<Guid> { target.Id }) : Ops.Replace(_doc.State, WithGeometry(target, g));
                _doc.Commit("Delete point", next);
                _node = null;
                return true;
            }
            case Key.S when target is not null && _node is { } n:
                _doc.Commit("Smooth/sharp point", Ops.Replace(_doc.State, WithGeometry(target, NodeEdit.ToggleSmooth(target.Geometry, n))));
                return true;
        }
        return false;
    }

    void DrawNodes(DrawingContext ctx)
    {
        var target = NodeTarget();
        if (target is null) return;
        var g = target.Geometry;
        var t = target.Transform;
        var accent = new SolidColorBrush(AccentColor);
        var edge = new Pen(accent, 1.2);
        var handleLine = new Pen(new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), 1);
        if (_node is { } sel && sel.Sub < g.Subpaths.Length && sel.Index < NodeEdit.AnchorCount(g.Subpaths[sel.Sub]))
        {
            var a = ToScreen(t.Apply(NodeEdit.Anchor(g, sel)));
            var (hin, hout) = NodeEdit.Handles(g, sel);
            foreach (var h in new[] { hin, hout })
                if (h is { } hv)
                {
                    var hs = ToScreen(t.Apply(hv));
                    ctx.DrawLine(handleLine, a, hs);
                    ctx.DrawEllipse(Brushes.White, edge, hs, 4, 4);
                }
        }
        foreach (var n in NodeEdit.Nodes(g))
        {
            var s = ToScreen(t.Apply(NodeEdit.Anchor(g, n)));
            bool selected = _node == n;
            bool smooth = NodeEdit.IsSmooth(g, n);
            var r = new Rect(s.X - 4, s.Y - 4, 8, 8);
            if (smooth) ctx.DrawEllipse(selected ? accent : Brushes.White, edge, s, 4.5, 4.5);
            else ctx.DrawRectangle(selected ? accent : Brushes.White, edge, r);
        }
    }
}
