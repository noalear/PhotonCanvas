using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace PhotonCanvas.App.View3D;

/// <summary>
/// Draws a <see cref="Scene3D"/> the way a camera sees it, fitted into a view.
/// <para>
/// It works out, for every dot on the screen (several per pixel, smoothed together), which face is
/// nearest there (a depth buffer), and draws only that. Sorting whole faces far to near and drawing
/// them over one another (the painter's way) goes wrong whenever a long face lies beside small ones or
/// pieces slot into each other: faces then pop in front of or behind each other as the model turns.
/// </para>
/// Keep one painter per view: it keeps its buffers, and redraws only when something has changed.
/// </summary>
public sealed class Model3DPainter
{
    /// <summary>Room left round the model when it's fitted into the view (left + right, top + bottom).</summary>
    const double MarginX = 30, MarginY = 40;

    // The screen at several samples per pixel: depth (bigger = nearer) and which face is nearest, for the solid
    // faces and, separately, for the ghosts in front of them; the colours; the ghosts' edges.
    float[] _depth = [], _gDepth = [];
    int[] _id = [], _gId = [];
    uint[] _col = [], _gEdge = [];
    int[] _px = [];
    // Where a face's outline crosses each row (a bucket per row).
    int[] _count = [], _at = [];
    double[] _xs = [];
    readonly List<double[]> _loops = [];
    readonly List<(double Z0, double Z1)> _loopZ = [];

    int _sw, _sh;
    bool _ghosts;
    double _ks, _ox, _oy;
    Vec3 _r, _u, _d;

    WriteableBitmap? _bitmap;
    object? _last;

    double _sampleScale = 1;

    /// <summary>How big the model was last drawn: screen units per mm.</summary>
    public double PxPerMm { get; private set; }

    /// <summary>Where a point of the model was in the view, the last time it was drawn (screen units).</summary>
    public Point ToView(Vec3 p) => new((_ox + p.Dot(_r) * _ks) / _sampleScale, (_oy - p.Dot(_u) * _ks) / _sampleScale);

    /// <summary>
    /// Draws <paramref name="scene"/> into <paramref name="view"/>'s bounds, filling them with
    /// <paramref name="background"/> first. Groups whose bit is set in <paramref name="hiddenGroups"/> are left out
    /// (but the model is still fitted as a whole). <paramref name="draft"/> draws quicker and rougher (while turning
    /// it). Returns false (having drawn only the background) when there's nothing to draw.
    /// </summary>
    public bool Draw(DrawingContext ctx, Visual view, Scene3D scene, Camera3D camera, IBrush background, ulong hiddenGroups = 0, bool draft = false)
    {
        var size = view.Bounds.Size;
        ctx.FillRectangle(background, new Rect(size));
        var bgColour = background is ISolidColorBrush solid ? solid.Color : Color.FromRgb(0x80, 0x80, 0x80);
        if (scene.IsEmpty || size.Width < 2 || size.Height < 2) return false;
        double scaling = TopLevel.GetTopLevel(view)?.RenderScaling ?? 1;
        if (!(scaling > 0)) scaling = 1;
        int w = Math.Max(1, (int)Math.Ceiling(size.Width * scaling)), h = Math.Max(1, (int)Math.Ceiling(size.Height * scaling));
        var key = (scene, scene.Version, camera, w, h, scaling, hiddenGroups, draft, bgColour);
        if (!key.Equals(_last) || _bitmap is null)
        {
            if (!Paint(scene, camera, size, scaling, w, h, bgColour, hiddenGroups, draft)) { _last = null; return false; }
            if (_bitmap is null || _bitmap.PixelSize.Width != w || _bitmap.PixelSize.Height != h || Math.Abs(_bitmap.Dpi.X - 96 * scaling) > 1e-6)
            {
                _bitmap?.Dispose();
                _bitmap = new WriteableBitmap(new PixelSize(w, h), new Vector(96 * scaling, 96 * scaling), PixelFormat.Bgra8888, AlphaFormat.Premul);
            }
            using (var fb = _bitmap.Lock())
                for (int y = 0; y < h; y++) System.Runtime.InteropServices.Marshal.Copy(_px, y * w, fb.Address + y * fb.RowBytes, w);
            _last = key;
        }
        ctx.DrawImage(_bitmap!, new Rect(size));
        return true;
    }

    /// <summary>
    /// The picture <see cref="Draw"/> would draw in a view <paramref name="width"/> × <paramref name="height"/> pixels
    /// (at 1:1), as 0xAARRGGBB, row by row; null when there's nothing to draw.
    /// </summary>
    public uint[]? Picture(Scene3D scene, Camera3D camera, int width, int height, Color background, ulong hiddenGroups = 0, bool draft = false)
    {
        _last = null;
        if (scene.IsEmpty || !Paint(scene, camera, new Size(width, height), 1, width, height, background, hiddenGroups, draft)) return null;
        var picture = new uint[width * height];
        for (int i = 0; i < picture.Length; i++) picture[i] = unchecked((uint)_px[i]);
        return picture;
    }

    /// <summary>Fills <see cref="_px"/> with the picture, w × h pixels.</summary>
    bool Paint(Scene3D scene, Camera3D cam, Size size, double scaling, int w, int h, Color background, ulong hidden, bool draft)
    {
        _r = cam.Right; _u = cam.Up; _d = cam.Toward;
        // Fit the whole model (hidden groups too, so the picture doesn't jump when they're hidden).
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        foreach (var f in scene.Faces)
            foreach (var l in f.Loops)
                foreach (var p in l)
                {
                    double x = p.Dot(_r), y = -p.Dot(_u);
                    x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); y0 = Math.Min(y0, y); y1 = Math.Max(y1, y);
                }
        double k = Math.Min((size.Width - MarginX) / Math.Max(1e-6, x1 - x0), (size.Height - MarginY) / Math.Max(1e-6, y1 - y0));
        if (!(k > 0) || double.IsInfinity(k)) return false;
        PxPerMm = k;
        // Samples per pixel each way: enough to smooth the edges; fewer on a big or sharp screen (to keep the
        // buffers a sensible size) and while turning (to keep up).
        int s = draft ? 1 : scaling >= 1.5 ? 2 : 3;
        while (s > 1 && (long)w * h * s * s > MaxSamples) s--;
        _sw = w * s; _sh = h * s;
        _sampleScale = scaling * s;
        _ks = k * scaling * s;
        _ox = _sw / 2.0 - (x0 + x1) / 2 * _ks;
        _oy = _sh / 2.0 - (y0 + y1) / 2 * _ks;

        int n = _sw * _sh;
        Ensure(ref _depth, n);
        Ensure(ref _id, n);
        Ensure(ref _col, n);
        bool Hidden(int group) => group is >= 0 and < 64 && (hidden >> group & 1) != 0;
        var faces = scene.Faces;
        var seen = new bool[faces.Count];
        bool ghosts = false;
        for (int i = 0; i < faces.Count; i++)
        {
            seen[i] = !Hidden(faces[i].Group) && faces[i].Normal.Dot(_d) > 1e-6;
            ghosts |= seen[i] && faces[i].Ghost;
        }
        Array.Fill(_depth, float.NegativeInfinity, 0, n);
        Array.Fill(_id, -1, 0, n);
        // The ghosts' buffers are only made, and looked at, when there are ghosts.
        if (ghosts)
        {
            Ensure(ref _gDepth, n);
            Ensure(ref _gId, n);
            Ensure(ref _gEdge, n);
            Array.Fill(_gDepth, float.NegativeInfinity, 0, n);
            Array.Fill(_gId, -1, 0, n);
            Array.Clear(_gEdge, 0, n);
        }
        _ghosts = ghosts;

        // The solid faces, then the ghosts in front of them.
        for (int i = 0; i < faces.Count; i++)
            if (seen[i] && !faces[i].Ghost) Face(faces[i], i, ghost: false);
        for (int i = 0; i < faces.Count; i++)
            if (seen[i] && faces[i].Ghost) Face(faces[i], i, ghost: true);

        // Each solid face's colour where it's nearest.
        var colours = faces.Select(f => Pack(f.Colour)).ToArray();
        uint bg = Pack(background) | 0xFF000000u;
        Parallel.For(0, _sh, row =>
        {
            for (int i = row * _sw, end = i + _sw; i < end; i++) _col[i] = _id[i] >= 0 ? colours[_id[i]] | 0xFF000000u : bg;
        });

        // What's engraved on the faces, shown only where its face is.
        foreach (var fill in scene.Fills)
        {
            if (fill.Face < 0 || fill.Face >= faces.Count || !seen[fill.Face] || faces[fill.Face].Ghost) continue;
            if (!Project(fill.Loops, out _, out _)) continue;
            Scan(Kind.Fill, fill.Face, default, Pack(fill.Colour));
        }

        // Scored lines on faces, then the edges: an edge shows where the face it borders is nearest, or where
        // nothing is in front of it.
        double sample = 1 / _ks;
        foreach (var line in scene.Lines)
        {
            if (Hidden(line.Group)) continue;
            bool ghost = line.OwnerA >= 0 && line.OwnerA < faces.Count && faces[line.OwnerA].Ghost
                      || line.OwnerB >= 0 && line.OwnerB < faces.Count && faces[line.OwnerB].Ghost;
            if (line.OnFace && (line.OwnerA < 0 || !seen[line.OwnerA])) continue;
            // A ghost's edges show through other ghosts (so its shape can be made out), but not those round its back.
            if (ghost && !ghosts) continue;
            if (ghost && line.OwnerA >= 0 && line.OwnerB >= 0 && !seen[line.OwnerA] && !seen[line.OwnerB]) continue;
            double hw = line.Width.ToPx(k) * scaling * s / 2;
            var mode = line.OnFace ? Kind.OnFace : ghost ? Kind.GhostEdge : Kind.Edge;
            var pen = new Pen3(mode, line.OwnerA, line.OwnerB, Pack(line.Colour), (float)(0.03 + 1.5 * sample));
            for (int j = 0; j + 1 < line.Points.Length; j++) Segment(line.Points[j], line.Points[j + 1], hw, pen);
        }

        // The samples of each pixel averaged, the ghosts laid over the solid faces.
        if (_px.Length != w * h) _px = new int[w * h];
        int ss = s * s, sw = _sw;
        var col = _col; var gId = _gId; var gEdge = _gEdge; var px = _px;
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                int r = 0, g = 0, b = 0;
                for (int sy = 0; sy < s; sy++)
                {
                    int i = (y * s + sy) * sw + x * s;
                    for (int sx = 0; sx < s; sx++, i++)
                    {
                        uint c = col[i];
                        if (ghosts)
                        {
                            if (gId[i] >= 0) c = Over(c, colours[gId[i]]);
                            if (gEdge[i] != 0) c = Over(c, gEdge[i]);
                        }
                        r += (int)(c >> 16 & 0xFF); g += (int)(c >> 8 & 0xFF); b += (int)(c & 0xFF);
                    }
                }
                px[y * w + x] = unchecked((int)(0xFF000000u | (uint)(r / ss) << 16 | (uint)(g / ss) << 8 | (uint)(b / ss)));
            }
        });
        return true;
    }

    /// <summary>The most samples drawn (so the buffers stay well under 100 MB).</summary>
    const long MaxSamples = 6_000_000;

    static void Ensure<T>(ref T[] buffer, int n)
    {
        if (buffer.Length < n) buffer = new T[n];
    }

    enum Kind { Solid, Ghost, Fill, OnFace, Edge, GhostEdge }

    /// <summary>A face's depth across the screen: z = A + Bx·x + By·y, kept within its corners' depths.</summary>
    readonly record struct Plane(double A, double Bx, double By, float Z0, float Z1);

    readonly record struct Pen3(Kind Mode, int OwnerA, int OwnerB, uint Colour, float Tolerance);

    void Face(Scene3D.Face f, int index, bool ghost)
    {
        if (!Project(f.Loops, out float z0, out float z1)) return;
        // The face's plane, from its own corners (Newell's way), turned the way it says it faces.
        var nn = Newell(f.Loops[0]);
        if (nn.Length < 1e-12) nn = f.Normal;
        nn = nn.Unit;
        if (nn.Dot(f.Normal) < 0) nn = -nn;
        double nd = nn.Dot(_d);
        if (nd < 1e-9) return;
        double nr = nn.Dot(_r), nu = nn.Dot(_u), c = nn.Dot(f.Loops[0][0]);
        // n·P = c with P = X·r + Yu·u + Z·d, X = (x − ox)/ks, Yu = (oy − y)/ks.
        var plane = new Plane((c + nr * _ox / _ks - nu * _oy / _ks) / nd, -nr / (nd * _ks), nu / (nd * _ks), z0, z1);
        Scan(ghost ? Kind.Ghost : Kind.Solid, index, plane, 0);
    }

    static Vec3 Newell(Vec3[] l)
    {
        double x = 0, y = 0, z = 0;
        for (int i = 0; i < l.Length; i++)
        {
            var a = l[i];
            var b = l[(i + 1) % l.Length];
            x += (a.Y - b.Y) * (a.Z + b.Z);
            y += (a.Z - b.Z) * (a.X + b.X);
            z += (a.X - b.X) * (a.Y + b.Y);
        }
        return new Vec3(x, y, z);
    }

    /// <summary>The loops onto the screen (into <see cref="_loops"/> as x, y pairs); false when none is on it.</summary>
    bool Project(Vec3[][] loops, out float zMin, out float zMax)
    {
        _loops.Clear();
        double lo = double.MaxValue, hi = double.MinValue;
        foreach (var l in loops)
        {
            var xy = new double[l.Length * 2];
            for (int i = 0; i < l.Length; i++)
            {
                var p = l[i];
                xy[2 * i] = _ox + p.Dot(_r) * _ks;
                xy[2 * i + 1] = _oy - p.Dot(_u) * _ks;
                double z = p.Dot(_d);
                lo = Math.Min(lo, z); hi = Math.Max(hi, z);
            }
            _loops.Add(xy);
        }
        zMin = (float)lo; zMax = (float)hi;
        return _loops.Count > 0;
    }

    /// <summary>Fills the projected loops (even-odd), sample by sample.</summary>
    void Scan(Kind kind, int face, Plane plane, uint colour)
    {
        double yMin = double.MaxValue, yMax = double.MinValue;
        foreach (var xy in _loops)
            for (int i = 1; i < xy.Length; i += 2) { yMin = Math.Min(yMin, xy[i]); yMax = Math.Max(yMax, xy[i]); }
        int r0 = Math.Max(0, (int)Math.Ceiling(yMin - 0.5)), r1 = Math.Min(_sh - 1, (int)Math.Ceiling(yMax - 0.5) - 1);
        if (r1 < r0) return;
        int rows = r1 - r0 + 1;
        if (_count.Length < rows + 1) { _count = new int[rows + 1]; _at = new int[rows + 1]; }
        Array.Clear(_count, 0, rows + 1);
        // How many edges cross each row (counted as steps up and down), then where each row's crossings go.
        foreach (var xy in _loops)
        {
            int m = xy.Length / 2;
            for (int i = 0; i < m; i++)
            {
                double ya = xy[2 * i + 1], yb = xy[2 * ((i + 1) % m) + 1];
                if (ya == yb) continue;
                int ra = Math.Max(r0, (int)Math.Ceiling(Math.Min(ya, yb) - 0.5)), rb = Math.Min(r1, (int)Math.Ceiling(Math.Max(ya, yb) - 0.5) - 1);
                if (rb < ra) continue;
                _count[ra - r0]++;
                _count[rb - r0 + 1]--;
            }
        }
        int total = 0, run = 0;
        for (int r = 0; r < rows; r++)
        {
            run += _count[r];
            _at[r] = total;
            total += run;
        }
        _at[rows] = total;
        if (total == 0) return;
        if (_xs.Length < total) _xs = new double[Math.Max(total, _xs.Length * 2)];
        // _count now says how many of each row's crossings are in.
        Array.Clear(_count, 0, rows + 1);
        foreach (var xy in _loops)
        {
            int m = xy.Length / 2;
            for (int i = 0; i < m; i++)
            {
                double xa = xy[2 * i], ya = xy[2 * i + 1];
                int j = (i + 1) % m;
                double xb = xy[2 * j], yb = xy[2 * j + 1];
                if (ya == yb) continue;
                int ra = Math.Max(r0, (int)Math.Ceiling(Math.Min(ya, yb) - 0.5)), rb = Math.Min(r1, (int)Math.Ceiling(Math.Max(ya, yb) - 0.5) - 1);
                double slope = (xb - xa) / (yb - ya);
                for (int r = ra; r <= rb; r++)
                {
                    int q = r - r0;
                    _xs[_at[q] + _count[q]++] = xa + (r + 0.5 - ya) * slope;
                }
            }
        }
        for (int q = 0; q < rows; q++)
        {
            int from = _at[q], cnt = _at[q + 1] - from;
            if (cnt < 2) continue;
            if (cnt == 2) { if (_xs[from] > _xs[from + 1]) (_xs[from], _xs[from + 1]) = (_xs[from + 1], _xs[from]); }
            else Array.Sort(_xs, from, cnt);
            int row = r0 + q;
            double yc = row + 0.5;
            for (int e = 0; e + 1 < cnt; e += 2)
            {
                int c0 = Math.Max(0, (int)Math.Ceiling(_xs[from + e] - 0.5)), c1 = Math.Min(_sw - 1, (int)Math.Ceiling(_xs[from + e + 1] - 0.5) - 1);
                if (c1 < c0) continue;
                Span(kind, face, plane, colour, row, c0, c1, yc);
            }
        }
    }

    void Span(Kind kind, int face, Plane plane, uint colour, int row, int c0, int c1, double yc)
    {
        int i = row * _sw + c0;
        switch (kind)
        {
            case Kind.Solid:
            {
                double z = plane.A + plane.By * yc + plane.Bx * (c0 + 0.5);
                for (int c = c0; c <= c1; c++, i++, z += plane.Bx)
                {
                    float zf = Math.Clamp((float)z, plane.Z0, plane.Z1);
                    if (zf > _depth[i]) { _depth[i] = zf; _id[i] = face; }
                }
                break;
            }
            case Kind.Ghost:
            {
                double z = plane.A + plane.By * yc + plane.Bx * (c0 + 0.5);
                for (int c = c0; c <= c1; c++, i++, z += plane.Bx)
                {
                    float zf = Math.Clamp((float)z, plane.Z0, plane.Z1);
                    if (zf > _depth[i] && zf > _gDepth[i]) { _gDepth[i] = zf; _gId[i] = face; }
                }
                break;
            }
            case Kind.Fill:
                for (int c = c0; c <= c1; c++, i++)
                    if (_id[i] == face) _col[i] = Over(_col[i], colour);
                break;
        }
    }

    /// <summary>A straight line from a to b, <paramref name="hw"/> samples either side of its middle, square-ended.</summary>
    void Segment(Vec3 a, Vec3 b, double hw, in Pen3 pen)
    {
        double xa = _ox + a.Dot(_r) * _ks, ya = _oy - a.Dot(_u) * _ks, za = a.Dot(_d);
        double xb = _ox + b.Dot(_r) * _ks, yb = _oy - b.Dot(_u) * _ks, zb = b.Dot(_d);
        double dx = xb - xa, dy = yb - ya, len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1e-9) { dx = 1e-9; len = 1e-9; }
        double ux = dx / len, uy = dy / len;
        bool alongX = Math.Abs(dx) >= Math.Abs(dy);
        // The ends pushed out by half the width, so lines meeting at a corner close it.
        double ex0 = xa - ux * hw, ey0 = ya - uy * hw, ex1 = xb + ux * hw, ey1 = yb + uy * hw;
        if (alongX)
        {
            if (ex0 > ex1) { (ex0, ex1) = (ex1, ex0); (ey0, ey1) = (ey1, ey0); }
            double spread = hw * len / Math.Max(Math.Abs(dx), 1e-9);
            int c0 = Math.Max(0, (int)Math.Ceiling(ex0 - 0.5)), c1 = Math.Min(_sw - 1, (int)Math.Ceiling(ex1 - 0.5) - 1);
            if (c1 < c0) { int c = (int)Math.Floor((ex0 + ex1) / 2); if (c < 0 || c >= _sw) return; c0 = c1 = c; }
            double slope = (ey1 - ey0) / Math.Max(ex1 - ex0, 1e-9);
            for (int c = c0; c <= c1; c++)
            {
                double xc = c + 0.5, yc = ey0 + (xc - ex0) * slope;
                double t = Math.Clamp(((xc - xa) * ux + (yc - ya) * uy) / len, 0, 1);
                float z = (float)(za + (zb - za) * t);
                int r0 = Math.Max(0, (int)Math.Ceiling(yc - spread - 0.5)), r1 = Math.Min(_sh - 1, (int)Math.Ceiling(yc + spread - 0.5) - 1);
                if (r1 < r0) { int r = (int)Math.Floor(yc); if (r < 0 || r >= _sh) continue; r0 = r1 = r; }
                for (int r = r0; r <= r1; r++) Plot(r * _sw + c, z, pen);
            }
        }
        else
        {
            if (ey0 > ey1) { (ex0, ex1) = (ex1, ex0); (ey0, ey1) = (ey1, ey0); }
            double spread = hw * len / Math.Max(Math.Abs(dy), 1e-9);
            int r0 = Math.Max(0, (int)Math.Ceiling(ey0 - 0.5)), r1 = Math.Min(_sh - 1, (int)Math.Ceiling(ey1 - 0.5) - 1);
            if (r1 < r0) { int r = (int)Math.Floor((ey0 + ey1) / 2); if (r < 0 || r >= _sh) return; r0 = r1 = r; }
            double slope = (ex1 - ex0) / Math.Max(ey1 - ey0, 1e-9);
            for (int r = r0; r <= r1; r++)
            {
                double yc = r + 0.5, xc = ex0 + (yc - ey0) * slope;
                double t = Math.Clamp(((xc - xa) * ux + (yc - ya) * uy) / len, 0, 1);
                float z = (float)(za + (zb - za) * t);
                int c0 = Math.Max(0, (int)Math.Ceiling(xc - spread - 0.5)), c1 = Math.Min(_sw - 1, (int)Math.Ceiling(xc + spread - 0.5) - 1);
                if (c1 < c0) { int c = (int)Math.Floor(xc); if (c < 0 || c >= _sw) continue; c0 = c1 = c; }
                for (int c = c0; c <= c1; c++) Plot(r * _sw + c, z, pen);
            }
        }
    }

    void Plot(int i, float z, in Pen3 pen)
    {
        switch (pen.Mode)
        {
            case Kind.OnFace:
                if (_id[i] == pen.OwnerA) _col[i] = Over(_col[i], pen.Colour);
                else if (_ghosts && _gId[i] == pen.OwnerA) _gEdge[i] = pen.Colour;
                break;
            case Kind.Edge:
                if (_id[i] == pen.OwnerA || _id[i] == pen.OwnerB || z >= _depth[i] - pen.Tolerance)
                {
                    _col[i] = Over(_col[i], pen.Colour);
                    // An edge in front of a ghost isn't behind it.
                    if (_ghosts && z >= _gDepth[i] - pen.Tolerance) { _gId[i] = -1; _gEdge[i] = 0; }
                }
                break;
            case Kind.GhostEdge:
                if (z >= _depth[i] - pen.Tolerance) _gEdge[i] = pen.Colour;
                break;
        }
    }

    static uint Pack(Color c) => (uint)c.A << 24 | (uint)c.R << 16 | (uint)c.G << 8 | c.B;

    /// <summary><paramref name="top"/> (with its own transparency) over an opaque colour.</summary>
    static uint Over(uint under, uint top)
    {
        uint a = top >> 24;
        if (a == 255) return top;
        if (a == 0) return under;
        uint na = 255 - a;
        uint r = ((top >> 16 & 0xFF) * a + (under >> 16 & 0xFF) * na + 127) / 255;
        uint g = ((top >> 8 & 0xFF) * a + (under >> 8 & 0xFF) * na + 127) / 255;
        uint b = ((top & 0xFF) * a + (under & 0xFF) * na + 127) / 255;
        return 0xFF000000u | r << 16 | g << 8 | b;
    }
}
