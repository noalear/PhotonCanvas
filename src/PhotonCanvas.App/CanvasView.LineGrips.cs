using System.Globalization;
using Avalonia;
using Avalonia.Media;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PathGeometry = PhotonCanvas.Doc.PathGeometry;

namespace PhotonCanvas.App;

/// <summary>
/// A selected straight line shows grips, as in AutoCAD, instead of the box round it: one at each end
/// and one in the middle. Dragging an end moves just that end (onto object snaps; Shift keeps 45° steps
/// from the other end); dragging the middle moves the selection, its middle landing on snap points.
/// </summary>
public sealed partial class CanvasView
{
    /// <summary>More lines than this selected show the box (as AutoCAD stops drawing grips).</summary>
    const int MaxGripLines = 100;

    /// <summary>A line's grip: 0 its start, 1 its middle, 2 its end.</summary>
    readonly record struct LineGrip(Guid Shape, int Which);

    LineGrip? _grip;
    Vec2 _gripA, _gripB;
    IReadOnlySet<Guid>? _gripSkip;

    /// <summary>The selected straight lines and their ends (world mm), when the selection is nothing else; else null.</summary>
    List<(PathShape Shape, Vec2 A, Vec2 B)>? SelectedLines()
    {
        if (_doc is null || _doc.Selection.Count is 0 or > MaxGripLines) return null;
        var lines = new List<(PathShape, Vec2, Vec2)>();
        foreach (var id in _doc.Selection)
        {
            if (_doc.State.FindShape(id) is not PathShape ps || ps is TextShape or CodeShape or ParamShape || ps.Locked) return null;
            if (ps.Geometry.Subpaths.Length != 1 || ps.Geometry.Subpaths[0] is not { Closed: false } sp || sp.Segs.Length != 1 || sp.Segs[0].Kind != SegKind.Line) return null;
            var w = ps.World.Subpaths[0];
            lines.Add((ps, w.Start, w.End));
        }
        return lines;
    }

    /// <summary>The grip under the pointer, or null.</summary>
    LineGrip? HitLineGrip(Point screen)
    {
        if (SelectedLines() is not { } lines) return null;
        foreach (var (s, a, b) in lines)
            foreach (var (p, which) in new[] { (a, 0), (b, 2), ((a + b) * 0.5, 1) })
            {
                var q = ToScreen(p);
                if (Math.Abs(screen.X - q.X) <= HandleSize && Math.Abs(screen.Y - q.Y) <= HandleSize) return new LineGrip(s.Id, which);
            }
        return null;
    }

    /// <summary>Starts dragging a grip; false when there's none under the pointer.</summary>
    bool LineGripPressed(Point screen)
    {
        if (_doc is null || HitLineGrip(screen) is not { } grip || SelectedLines()?.FirstOrDefault(l => l.Shape.Id == grip.Shape) is not { Shape: not null } line) return false;
        _grip = grip;
        _gripA = line.A;
        _gripB = line.B;
        _drag = Drag.LineGrip;
        _dragIds = [.. _doc.Selection];
        _dragBounds = SelectionBounds(_doc);
        _base = _doc.State;
        // An end snaps onto anything but its own line; the middle onto anything not being moved.
        _gripSkip = grip.Which == 1 ? _dragIds : new HashSet<Guid> { grip.Shape };
        return true;
    }

    void LineGripMoved(Point screen, Vec2 w)
    {
        if (_doc is null || _base is null || _grip is not { } grip || !StartEditIfMoved(screen)) return;
        var shift = w - _pressWorld;
        if (grip.Which == 1)
        {
            var mid = (_gripA + _gripB) * 0.5;
            Vec2 d;
            if (_lastMods.HasFlag(Avalonia.Input.KeyModifiers.Shift)) d = Math.Abs(shift.X) >= Math.Abs(shift.Y) ? new(shift.X, 0) : new(0, shift.Y);
            else d = SnapPick(mid + shift, _gripSkip) - mid;
            _doc.UpdateEdit(Ops.Move(_base, _dragIds, d.X, d.Y));
            return;
        }
        if (_base.FindShape(grip.Shape) is not PathShape line) return;
        var stays = grip.Which == 0 ? _gripB : _gripA;
        var moves = grip.Which == 0 ? _gripA : _gripB;
        var to = LineEnd(stays, SnapPick(moves + shift, _gripSkip, stays));
        // The line keeps its direction: its start stays its start.
        var g = grip.Which == 0
            ? PathGeometry.Line(LocalOf(line, to), LocalOf(line, stays))
            : PathGeometry.Line(LocalOf(line, stays), LocalOf(line, to));
        _doc.UpdateEdit(Ops.Replace(_base, WithGeometry(line, g)));
    }

    /// <summary>The grips (the middle one filled: it moves the line), and one line's length and angle.</summary>
    void DrawLineGrips(DrawingContext ctx, List<(PathShape Shape, Vec2 A, Vec2 B)> lines)
    {
        var move = new SolidColorBrush(AccentColor);
        foreach (var (_, a, b) in lines)
            foreach (var (p, middle) in new[] { (a, false), (b, false), ((a + b) * 0.5, true) })
            {
                var q = ToScreen(p);
                ctx.DrawRectangle(middle ? move : HandleFill, HandlePen, new Rect(q.X - HandleSize / 2, q.Y - HandleSize / 2, HandleSize, HandleSize), 1.5, 1.5);
            }
        if (lines.Count != 1) return;
        var (_, s, e) = lines[0];
        var d = e - s;
        double deg = (Math.Atan2(-d.Y, d.X) * 180 / Math.PI + 360) % 360;
        var ft = Text($"{Units.Len(d.Length)}  ∠ {deg:0.#}°", 11, Brushes.White);
        var m = ToScreen((s + e) * 0.5);
        var lr = new Rect(m.X - ft.Width / 2 - 6, m.Y + 12, ft.Width + 12, ft.Height + 4);
        ctx.FillRectangle(move, lr, 4);
        ctx.DrawText(ft, new Point(lr.X + 6, lr.Y + 2));
    }
}
