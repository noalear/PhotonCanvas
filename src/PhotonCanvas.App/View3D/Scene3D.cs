using Avalonia.Media;
using Clipper2Lib;

namespace PhotonCanvas.App.View3D;

/// <summary>A point or direction in a model's space (mm).</summary>
public readonly record struct Vec3(double X, double Y, double Z)
{
    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator -(Vec3 a) => new(-a.X, -a.Y, -a.Z);
    public static Vec3 operator *(Vec3 a, double k) => new(a.X * k, a.Y * k, a.Z * k);
    public double Dot(Vec3 b) => X * b.X + Y * b.Y + Z * b.Z;
    public Vec3 Cross(Vec3 b) => new(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);
    public double Length => Math.Sqrt(Dot(this));
    public Vec3 Unit { get { double l = Length; return l > 0 ? this * (1 / l) : this; } }
}

/// <summary>
/// Where a flat piece sits: its flat (u, v) coordinates run along <see cref="U"/> and <see cref="V"/> from
/// <see cref="O"/>, and w runs through its thickness along <see cref="N"/> (= U × V).
/// </summary>
public readonly record struct Frame3(Vec3 O, Vec3 U, Vec3 V)
{
    public Vec3 N => U.Cross(V);
    public Vec3 At(double u, double v, double w = 0) => O + U * u + V * v + N * w;
}

/// <summary>How wide a line is drawn on the screen: <see cref="Mm"/> of the model, kept between <see cref="MinPx"/> and <see cref="MaxPx"/> (screen units).</summary>
public readonly record struct LineWidth(double Mm, double MinPx, double MaxPx)
{
    /// <summary>The same width on the screen however big the model is drawn.</summary>
    public static LineWidth Px(double px) => new(0, px, px);

    public double ToPx(double pxPerMm) => Math.Clamp(pxPerMm * Mm, MinPx, MaxPx);
}

/// <summary>How a piece (<see cref="Scene3D.AddSlab"/>) is drawn.</summary>
/// <param name="Paint">The colour of a face, given the way it faces (outwards, a unit vector) and whether it's one of the edges round the piece (true) or one of its two faces.</param>
/// <param name="Edge">The lines round the piece's faces and down its corners; transparent for none.</param>
/// <param name="EdgeWidth">How wide those lines are.</param>
/// <param name="Etched">The colour of what's engraved on its top face.</param>
/// <param name="Score">The colour of the lines scored on its top face.</param>
/// <param name="ScoreWidth">How wide the scored lines are.</param>
/// <param name="CornerDeg">Down the edge of the piece, a line is drawn where the outline turns by more than this; smoothly curved edges get none.</param>
public sealed record SlabStyle(Func<Vec3, bool, Color> Paint, Color Edge, LineWidth EdgeWidth, Color Etched, Color Score, LineWidth ScoreWidth, double CornerDeg = 25)
{
    /// <summary>
    /// The colour of the sheet itself, unlit, for the realistic look (<see cref="Model3DPainter"/>), which lights it
    /// itself. Unset, it's the brightest colour <see cref="Paint"/> gives any way the face turns: the colour a
    /// <see cref="Lit"/> piece has facing straight at its light.
    /// </summary>
    public Color? Sheet { get; init; }

    /// <summary>The sheet's own colour: <see cref="Sheet"/>, or the brightest <see cref="Paint"/> gives.</summary>
    public Color SheetColour()
    {
        if (Sheet is { } sheet) return sheet;
        Color best = Paint(new Vec3(0, 1, 0), false);
        int bestSum = best.R + best.G + best.B;
        // Directions spread evenly over a sphere (a Fibonacci spiral).
        const int n = 96;
        for (int i = 0; i < n; i++)
        {
            double y = 1 - 2 * (i + 0.5) / n, r = Math.Sqrt(1 - y * y), a = i * 2.399963229728653;
            var c = Paint(new Vec3(r * Math.Cos(a), y, r * Math.Sin(a)), false);
            int sum = c.R + c.G + c.B;
            if (sum > bestSum) { best = c; bestSum = sum; }
        }
        return best;
    }

    /// <summary>
    /// The usual look: <paramref name="colour"/> lit from <paramref name="light"/> (ambient + diffuse), the edges round
    /// the piece a little darker (<paramref name="sideShade"/>).
    /// </summary>
    public static Func<Vec3, bool, Color> Lit(Color colour, Vec3 light, double ambient = 0.62, double diffuse = 0.38, double sideShade = 0.86)
    {
        var l = light.Unit;
        return (n, side) => Scene3D.Shade(colour, (ambient + diffuse * Math.Max(0, n.Dot(l))) * (side ? sideShade : 1));
    }
}

/// <summary>
/// A model to draw in 3D: flat faces (with holes), with what's engraved and scored on them, and lines
/// along their edges. <see cref="Model3DPainter"/> draws it with a depth buffer, so whatever is nearest
/// is what shows, whichever way it is turned: faces never pop in front of one another.
/// <para>
/// Every face, line and fill belongs to a group (0–63); the painter can leave groups out (a floor
/// lifted off to see under it) while still fitting the whole model in the view.
/// </para>
/// </summary>
public sealed class Scene3D
{
    internal sealed record Face(Vec3[][] Loops, Vec3 Normal, Color Colour, int Group, Surface? Look = null)
    {
        public bool Ghost => Colour.A < 255;
        /// <summary>What the face is part of, for the view to say what was clicked (<see cref="Model3DPainter.HitAt"/>).</summary>
        public object? Tag { get; init; }
    }

    internal sealed record Fill(int Face, Vec3[][] Loops, Color Colour);

    internal sealed record Line(Vec3[] Points, Color Colour, LineWidth Width, int OwnerA, int OwnerB, int Group, bool OnFace);

    internal readonly List<Face> Faces = [];
    internal readonly List<Fill> Fills = [];
    internal readonly List<Line> Lines = [];

    /// <summary>Changes each time something is added, so a painter knows to draw again.</summary>
    public int Version { get; private set; }

    public bool IsEmpty => Faces.Count == 0;

    public void Clear()
    {
        Faces.Clear();
        Fills.Clear();
        Lines.Clear();
        Version++;
    }

    /// <summary>
    /// A flat face: its outlines (holes included, filled even-odd), seen only from the side
    /// <paramref name="normal"/> points to. A colour that isn't fully opaque makes it a ghost: drawn faint
    /// over what's behind it, and never hiding anything. Returns the face's number, for what goes on it.
    /// </summary>
    /// <para><paramref name="surface"/> says what it's made of, for the realistic look; without it, that look paints it <paramref name="colour"/>.</para>
    public int AddFace(IEnumerable<Vec3[]> loops, Vec3 normal, Color colour, int group = 0, object? tag = null, Surface? surface = null)
    {
        var ls = loops.Where(l => l.Length >= 3).ToArray();
        Faces.Add(new Face(ls, normal.Unit, colour, group, colour.A < 255 ? null : surface) { Tag = tag });
        Version++;
        return Faces.Count - 1;
    }

    /// <summary>What a face was added for (the <c>tag</c> given with it), or null.</summary>
    public object? TagOf(int face) => face >= 0 && face < Faces.Count ? Faces[face].Tag : null;

    /// <summary>A filled area drawn on a face (an engraving): shows only where that face does.</summary>
    public void AddFill(int face, IEnumerable<Vec3[]> loops, Color colour)
    {
        var ls = loops.Where(l => l.Length >= 3).ToArray();
        if (ls.Length == 0) return;
        Fills.Add(new Fill(face, ls, colour));
        Version++;
    }

    /// <summary>A line drawn on a face (a scored line): shows only where that face does.</summary>
    public void AddLine(int face, Vec3[] points, Color colour, LineWidth width, bool closed = false)
    {
        if (points.Length < 2) return;
        Lines.Add(new Line(closed ? [.. points, points[0]] : points, colour, width, face, face, Faces[face].Group, true));
        Version++;
    }

    /// <summary>
    /// A line in the model (an edge): shown where nothing is in front of it. Along the edge of a face, name
    /// the face(s) it borders as owners, so it isn't hidden by them.
    /// </summary>
    public void AddEdge(Vec3 a, Vec3 b, Color colour, LineWidth width, int ownerA = -1, int ownerB = -1, int group = 0)
    {
        Lines.Add(new Line([a, b], colour, width, ownerA, ownerB, group, false));
        Version++;
    }

    /// <summary>
    /// A flat piece cut from a sheet: <paramref name="outline"/> (outlines anticlockwise, holes clockwise, in the
    /// piece's own u, v) placed by <paramref name="frame"/>, from <paramref name="w0"/> to <paramref name="w1"/>
    /// through its thickness (w1 is the top: what's <paramref name="etched"/> and <paramref name="scored"/> goes on it;
    /// <paramref name="backEtched"/> and <paramref name="backScored"/> go on the bottom, in the same u, v, as seen through the piece).
    /// <paramref name="faces"/> and <paramref name="sides"/> leave out its two faces or its edges. With
    /// <paramref name="etchDepth"/> above 0 what's etched is sunk that deep into the piece, with the sides of the
    /// grooves lit as they face, so you can see how the engraving will stand out; otherwise it's painted on.
    /// Every face of it carries <paramref name="tag"/>, so a click on it can say which piece it was.
    /// </summary>
    public void AddSlab(PathsD outline, Frame3 frame, double w0, double w1, SlabStyle style, PathsD? etched = null, IEnumerable<PathD>? scored = null,
        int group = 0, bool faces = true, bool sides = true, double etchDepth = 0, PathsD? backEtched = null, IEnumerable<PathD>? backScored = null, object? tag = null)
    {
        var loops = outline.Where(c => c.Count >= 3).ToList();
        if (loops.Count == 0) return;
        if (!(w1 > w0)) throw new ArgumentException("A piece's top (w1) must be above its bottom (w0).", nameof(w1));
        var n = frame.N.Unit;
        Vec3 At(PointD p, double w) => frame.At(p.x, p.y, w);
        bool edged = style.Edge.A > 0;
        int top = -1, bottom = -1;
        // What it's made of, for the realistic look: the sheet's two faces with its grain along u, its laser-cut
        // edges charred, what's engraved burnt.
        var sheet = style.SheetColour();
        var grain = new Surface(sheet, SurfaceKind.Grain, frame.O, frame.U.Unit, frame.V.Unit);
        var burnt = new Surface(style.Etched, SurfaceKind.Burnt, frame.O, frame.U.Unit, frame.V.Unit);
        Surface CutEdge(Vec3 a, Vec3 b) => new(sheet, SurfaceKind.Cut, a, (b - a).Unit, n, w1 - w0);
        bool frontArt = etched is { Count: > 0 }, backArt = backEtched is { Count: > 0 };
        // Etched on both faces, each groove can only go a little under half way through.
        double depth = Math.Min(etchDepth, (w1 - w0) * (frontArt && backArt ? 0.45 : 0.9));

        // One face (at w, facing out along `outward`), with what's etched and scored on it.
        int Face(PathsD? art, IEnumerable<PathD>? lines, double w, Vec3 outward)
        {
            int face;
            if (depth > 0 && art is { Count: > 0 })
            {
                // Sunk in: the face with the grooves as holes, their floors further in, and their sides.
                double floor = w - Math.Sign(outward.Dot(n)) * depth;
                var grooves = Clipper.Union(Clipper.SimplifyPaths(new PathsD(art.Where(c => c.Count >= 3)), 0.08), Clipper2Lib.FillRule.NonZero)
                    .Where(c => c.Count >= 3 && Math.Abs(Clipper.Area(c)) > 0.01).ToList();
                face = AddFace(loops.Concat(grooves).Select(c => c.Select(p => At(p, w)).ToArray()), outward, style.Paint(outward, false), group, tag, grain);
                AddFace(grooves.Select(c => c.Select(p => At(p, floor)).ToArray()), outward, Shade(style.Etched, 0.92), group, tag, burnt);
                foreach (var c in grooves)
                {
                    // Outer edges of an etched area run anticlockwise, the edges of islands in it clockwise: the
                    // groove's side faces into the groove, to the left of the edge.
                    for (int i = 0; i < c.Count; i++)
                    {
                        var a = c[i];
                        var b = c[(i + 1) % c.Count];
                        double dx = b.x - a.x, dy = b.y - a.y, len = Math.Sqrt(dx * dx + dy * dy);
                        if (len < 1e-6) continue;
                        var into = (frame.U * (-dy / len) + frame.V * (dx / len)).Unit;
                        AddFace([[At(a, w), At(a, floor), At(b, floor), At(b, w)]], into, Shade(style.Paint(into, true), 0.9), group, tag, burnt with { Albedo = Shade(style.Etched, 0.8) });
                    }
                }
            }
            else
            {
                face = AddFace(loops.Select(c => c.Select(p => At(p, w)).ToArray()), outward, style.Paint(outward, false), group, tag, grain);
                if (art is { Count: > 0 }) AddFill(face, art.Where(c => c.Count >= 3).Select(c => c.Select(p => At(p, w)).ToArray()), style.Etched);
            }
            if (lines is not null)
                foreach (var l in lines)
                    if (l.Count >= 2) AddLine(face, [.. l.Select(p => At(p, w))], style.Score, style.ScoreWidth);
            return face;
        }

        if (faces)
        {
            top = Face(etched, scored, w1, n);
            bottom = Face(backEtched, backScored, w0, -n);
        }
        double cornerCos = Math.Cos(style.CornerDeg * Math.PI / 180);
        foreach (var c in loops)
        {
            // Drop repeated points, so every edge has a direction.
            var pts = new List<PointD>(c.Count);
            foreach (var p in c)
                if (pts.Count == 0 || Math.Abs(p.x - pts[^1].x) + Math.Abs(p.y - pts[^1].y) > 1e-6) pts.Add(p);
            while (pts.Count > 1 && Math.Abs(pts[0].x - pts[^1].x) + Math.Abs(pts[0].y - pts[^1].y) <= 1e-6) pts.RemoveAt(pts.Count - 1);
            int m = pts.Count;
            if (m < 3) continue;
            var sideFace = new int[m];
            var dir = new (double X, double Y)[m];
            for (int i = 0; i < m; i++)
            {
                var a = pts[i];
                var b = pts[(i + 1) % m];
                double dx = b.x - a.x, dy = b.y - a.y, len = Math.Sqrt(dx * dx + dy * dy);
                dir[i] = (dx / len, dy / len);
                // Outward: to the right of the edge, as outlines run anticlockwise and holes clockwise.
                var outward = (frame.U * (dy / len) + frame.V * (-dx / len)).Unit;
                sideFace[i] = sides ? AddFace([[At(a, w1), At(b, w1), At(b, w0), At(a, w0)]], outward, style.Paint(outward, true), group, tag, CutEdge(At(a, w0), At(b, w0))) : -1;
            }
            if (!edged) continue;
            for (int i = 0; i < m; i++)
            {
                var a = pts[i];
                var b = pts[(i + 1) % m];
                if (faces || sides)
                {
                    AddEdge(At(a, w1), At(b, w1), style.Edge, style.EdgeWidth, top, sideFace[i], group);
                    AddEdge(At(a, w0), At(b, w0), style.Edge, style.EdgeWidth, bottom, sideFace[i], group);
                }
                // Down the corner at a (between the edge before it and this one), only where the outline turns sharply.
                if (sides)
                {
                    int prev = (i + m - 1) % m;
                    if (dir[prev].X * dir[i].X + dir[prev].Y * dir[i].Y < cornerCos)
                        AddEdge(At(a, w1), At(a, w0), style.Edge, style.EdgeWidth, sideFace[prev], sideFace[i], group);
                }
            }
        }
    }

    /// <summary>A colour made lighter or darker by <paramref name="k"/> (its transparency kept).</summary>
    public static Color Shade(Color c, double k) =>
        Color.FromArgb(c.A, (byte)Math.Clamp(c.R * k, 0, 255), (byte)Math.Clamp(c.G * k, 0, 255), (byte)Math.Clamp(c.B * k, 0, 255));
}

/// <summary>What a face is made of, for the realistic look (<see cref="Model3DPainter"/>).</summary>
public enum SurfaceKind
{
    /// <summary>One colour, a little shiny.</summary>
    Plain,
    /// <summary>The face of a wooden sheet: grain running along <see cref="Surface.Along"/>.</summary>
    Grain,
    /// <summary>An edge the laser cut: charred dark, with the sheet's plies showing across it (along <see cref="Surface.Across"/>, through the sheet).</summary>
    Cut,
    /// <summary>Engraved: burnt, rough and mottled.</summary>
    Burnt,
}

/// <summary>
/// What a face is made of, for the realistic look: its own colour (unlit) and kind, and where its texture is laid,
/// from <see cref="Origin"/> along <see cref="Along"/> and <see cref="Across"/> (unit vectors, mm). A cut edge's
/// origin is on the sheet's bottom face, and <see cref="Thickness"/> is how thick the sheet is (its plies split it).
/// </summary>
public readonly record struct Surface(Color Albedo, SurfaceKind Kind, Vec3 Origin, Vec3 Along, Vec3 Across, double Thickness = 0);

/// <summary>Which way the model is seen: screen right, screen up, and towards the viewer (unit vectors at right angles).</summary>
public readonly record struct Camera3D(Vec3 Right, Vec3 Up, Vec3 Toward)
{
    /// <summary>Which way is up in the model (for the realistic look's light and the table it stands on); unset, Y.</summary>
    public Vec3 Sky { get; init; }

    /// <summary><see cref="Sky"/>, Y when it's unset.</summary>
    public Vec3 SkyOrY => Sky.Length > 0 ? Sky.Unit : new Vec3(0, 1, 0);

    /// <summary>For a model with Y up: turned round by <paramref name="yawDeg"/>, looking down from <paramref name="pitchDeg"/> above level.</summary>
    public static Camera3D YUp(double yawDeg, double pitchDeg)
    {
        double y = yawDeg * Math.PI / 180, p = pitchDeg * Math.PI / 180;
        var d = new Vec3(Math.Sin(y) * Math.Cos(p), Math.Sin(p), Math.Cos(y) * Math.Cos(p));
        var r = new Vec3(Math.Cos(y), 0, -Math.Sin(y));
        return new(r, d.Cross(r), d) { Sky = new Vec3(0, 1, 0) };
    }

    /// <summary>For a model with Z up, seen from the −Y side: turned round by <paramref name="yawDeg"/>, looking down from <paramref name="pitchDeg"/>.</summary>
    public static Camera3D ZUp(double yawDeg, double pitchDeg)
    {
        double y = yawDeg * Math.PI / 180, p = pitchDeg * Math.PI / 180;
        var d = new Vec3(Math.Sin(y) * Math.Cos(p), -Math.Cos(y) * Math.Cos(p), Math.Sin(p));
        var r = new Vec3(Math.Cos(y), Math.Sin(y), 0);
        return new(r, d.Cross(r), d) { Sky = new Vec3(0, 0, 1) };
    }
}
