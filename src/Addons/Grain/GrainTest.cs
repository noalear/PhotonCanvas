using System.Collections.Immutable;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Core.Imaging;

namespace PhotonCanvas.Addon.Grain;

/// <summary>
/// The grain test: two strips burned across the grain without any compensation, one at a layer's
/// power and one at <see cref="LowFactor"/> of it. A camera picture afterwards shows how much darker
/// dark grain came out, and how much darker the extra power made it; together they say how much
/// less power dark grain needs (<see cref="Read"/>).
/// </summary>
public static class GrainTest
{
    /// <summary>The second strip's share of the power.</summary>
    public const double LowFactor = 0.7;

    /// <summary>Each strip's size, mm: long, so it crosses many grain lines when laid across them.</summary>
    public const double StripLongMm = 40, StripWideMm = 15, GapMm = 3;

    /// <summary>Measured this far inside each strip's edge (edges burn differently), mm.</summary>
    public const double InsetMm = 1.5;

    /// <summary>
    /// The project with a grain test for layer <paramref name="layerId"/> added: two new layers right
    /// after it, each with one strip, centred on <paramref name="centre"/>. The strips run at right angles
    /// to <paramref name="grainAngleDeg"/> (across the grain; NaN = up and down). Returns the new state and
    /// the strips' ids.
    /// </summary>
    /// <param name="testOf">The layer the result is for (default: <paramref name="layerId"/>; another page's layer when the test is on a page of its own).</param>
    public static (DocState State, List<Guid> Strips) Add(DocState s, Guid layerId, Vec2 centre, double grainAngleDeg, Guid? testOf = null)
    {
        var tested = s.FindLayer(layerId) ?? throw new ArgumentException("No such layer.", nameof(layerId));
        int at = s.LayerIndex(layerId);
        var layers = s.Layers.ToBuilder();
        var shapes = s.Shapes.ToBuilder();
        var ids = new List<Guid>();
        var marks = new List<(Guid Layer, double Factor)>();
        double across = double.IsNaN(grainAngleDeg) ? 90 : grainAngleDeg + 90;
        int k = 0;
        foreach (double factor in new[] { 1.0, LowFactor })
        {
            var layer = TestLayer(tested, factor, s.Layers.Count + k);
            layers.Insert(at + 1 + k, layer);
            marks.Add((layer.Id, factor));
            // Side by side, each long way across the grain.
            double off = (k - 0.5) * (StripWideMm + GapMm);
            var local = PathGeometry.Rect(-StripWideMm / 2, -StripLongMm / 2, StripWideMm, StripLongMm);
            var t = Affine.Translate(off, 0).Then(Affine.Rotate(across - 90)).Then(Affine.Translate(centre));
            var strip = new PathShape { LayerId = layer.Id, Name = $"Grain test {factor * 100:0}%", Geometry = local, Transform = t };
            shapes.Add(strip);
            ids.Add(strip.Id);
            k++;
        }
        var next = s with { Layers = layers.ToImmutable(), Shapes = shapes.ToImmutable() };
        // Marked as test strips: never compensated, and found again when the test is read.
        foreach (var (id, f) in marks) next = GrainProject.WithLayer(next, id, new GrainSettings(GrainMode.Off, 0, true, f, testOf ?? layerId));
        return (next, ids);
    }

    /// <summary>A fill layer burning like <paramref name="tested"/> at <paramref name="factor"/> of its power, marked as a test strip.</summary>
    static Layer TestLayer(Layer tested, double factor, int index)
    {
        var laser = tested.Laser with { Power = Math.Round(tested.Laser.Power * factor, 1) };
        var l = Layer.Default(index, LayerMode.Fill) with { Name = $"Grain test {factor * 100:0}%", Laser = laser };
        return tested.Mode == LayerMode.Fill
            ? tested with { Id = l.Id, Name = l.Name, Laser = laser, ParentId = null, Color = l.Color, Output = true, Visible = true, Locked = false, FillStyle = FillStyle.Lines, RunCount = 0 }
            // A picture layer: tested as a fill as dense as its dots.
            : l with { LinesPerCm = Math.Clamp(tested.Dpi / 2.54, 10, 400), FocusOffsetMm = tested.FocusOffsetMm, Material = tested.Material, Reflective = tested.Reflective };
    }

    /// <summary>The project's test strips (the page on screen first, then the others): each with the share of the power it was burned at.</summary>
    public static List<(PathShape Strip, double Factor)> Strips(DocState s)
    {
        var o = new List<(PathShape, double)>();
        var all = GrainProject.All(s);
        foreach (var (layers, shapes) in new[] { (s.Layers, s.Shapes) }.Concat(s.OtherPages.Select(p => (p.Layers, p.Shapes))))
        {
            foreach (var l in layers)
                if (all.GetValueOrDefault(l.Id)?.TestFactor is { } f)
                    foreach (var sh in shapes.Where(x => x.LayerId == l.Id).OfType<PathShape>()) o.Add((sh, f));
            // One page's test at a time: the first page that has one.
            if (o.Count > 0) break;
        }
        return o;
    }

    /// <summary>The layer the project's test is for (null if there's no test, or it doesn't say).</summary>
    public static Guid? TestedLayer(DocState s) => GrainProject.All(s).Values.FirstOrDefault(v => v.TestFactor is not null)?.TestOf;

    /// <summary>
    /// The test on a page of its own (a page is its own job, so it's burned without the design):
    /// a new page after this one, holding only the two strips, shown.
    /// </summary>
    public static (DocState State, List<Guid> Strips) AddOnOwnPage(DocState s, Guid layerId, Vec2 centre, double grainAngleDeg)
    {
        int index = s.LayerIndex(layerId);
        if (index < 0) throw new ArgumentException("No such layer.", nameof(layerId));
        var page = Pages.Add(s, "Grain test");
        var copy = page.Layers[index];
        var (withTest, strips) = Add(page, copy.Id, centre, grainAngleDeg, layerId);
        var keep = strips.Select(id => withTest.FindShape(id)!.LayerId).ToHashSet();
        return (withTest with { Layers = [.. withTest.Layers.Where(l => keep.Contains(l.Id))] }, strips);
    }

    /// <summary>What reading a test found: the suggested settings, or why there are none.</summary>
    public sealed record Result(GrainCalibration? Calibration, string Message, double GrainEffect = 0, double PowerEffect = 0);

    /// <summary>
    /// Read the test from a camera picture taken after it was burned (<paramref name="after"/>,
    /// straightened, covering <paramref name="afterArea"/>), against the grain map made before.
    /// Fits darkness = a + b × grain + c × power and suggests b / c as the strength.
    /// </summary>
    public static Result Read(GrainMap map, IReadOnlyList<(PathShape Strip, double Factor)> strips, RasterImage after, Rect2 afterArea, DateTimeOffset? now = null)
    {
        if (strips.Select(x => x.Factor).Distinct().Count() < 2)
            return new(null, "The test needs both strips (full and lower power). Add the grain test again, burn it and take a new camera picture.");
        var lum = after.Luminance();
        double step = Math.Max(map.CellMm, 0.5);
        // Sums for the least-squares fit of D = a + b t + c f.
        var rows = new List<(double T, double F, double D)>();
        foreach (var (strip, factor) in strips)
        {
            var polys = strip.World.Flatten(0.05).Where(f => f.Closed && f.Points.Count >= 3).Select(f => f.Points).ToList();
            if (polys.Count == 0) continue;
            var b = strip.Bounds;
            for (double y = b.MinY + step / 2; y < b.MaxY; y += step)
                for (double x = b.MinX + step / 2; x < b.MaxX; x += step)
                {
                    var p = new Vec2(x, y);
                    if (polys.Count(poly => Geom.Inside(poly, p)) % 2 == 0) continue;
                    if (polys.Any(poly => EdgeDistance(poly, p) < InsetMm)) continue;
                    double t = map.At(p);
                    float logL = GrainMap.SampleLogAt(after, lum, afterArea, p);
                    if (double.IsNaN(t) || float.IsNaN(logL)) continue;
                    rows.Add((t, factor, -logL));
                }
        }
        int n = rows.Count;
        if (n < 50)
            return new(null, n == 0
                ? "The strips aren't on the grain map or the camera picture. Map the grain where the strips are, burn them, then take a new camera picture."
                : $"Only {n} places on the strips could be measured. Make sure the grain map and the new camera picture both cover the strips.");
        var (a, bt, cf, r2) = Fit(rows);
        // Brighter where burned harder means the picture isn't of the burned strips.
        if (!(cf > 0.02))
            return new(null, "The stronger strip didn't come out clearly darker than the weaker one, so the test can't tell how much power the grain is worth. Take the camera picture after the strips are burned, or burn the test at more power.", bt, cf);
        double strength = Math.Abs(bt) / cf;
        var cal = new GrainCalibration(Math.Min(strength, GrainSettings.MaxStrength), bt >= 0, r2, n, now ?? DateTimeOffset.Now);
        string which = bt >= 0 ? "Dark grain came out darker" : "Light grain came out darker";
        string msg = strength > GrainSettings.MaxStrength
            ? $"{which}, by more than {GrainSettings.MaxStrength:P0} less power can make up for. The strength is set to the most allowed; the result will be more even, but not fully."
            : strength < 0.03
                ? "The grain hardly changed how dark the strips came out: this wood engraves evenly as it is."
                : $"{which}: about {strength:P0} less power there evens it out.";
        return new(cal, msg, bt, cf);
    }

    /// <summary>Least squares of D on t and f: the constant, the grain's and the power's effect, and R².</summary>
    internal static (double A, double B, double C, double R2) Fit(IReadOnlyList<(double T, double F, double D)> rows)
    {
        // Normal equations for [1, t, f].
        double s1 = rows.Count, st = 0, sf = 0, stt = 0, stf = 0, sff = 0, sd = 0, std = 0, sfd = 0;
        foreach (var (t, f, d) in rows)
        {
            st += t; sf += f; stt += t * t; stf += t * f; sff += f * f;
            sd += d; std += t * d; sfd += f * d;
        }
        var m = new double[3, 3] { { s1, st, sf }, { st, stt, stf }, { sf, stf, sff } };
        var v = new[] { sd, std, sfd };
        var x = Solve3(m, v);
        if (x is null) return (0, 0, 0, 0);
        double mean = sd / s1, ssTot = 0, ssRes = 0;
        foreach (var (t, f, d) in rows)
        {
            double e = d - (x[0] + x[1] * t + x[2] * f);
            ssRes += e * e;
            ssTot += (d - mean) * (d - mean);
        }
        return (x[0], x[1], x[2], ssTot > 1e-12 ? Math.Max(0, 1 - ssRes / ssTot) : 0);
    }

    static double[]? Solve3(double[,] m, double[] v)
    {
        var a = (double[,])m.Clone();
        var b = (double[])v.Clone();
        for (int col = 0; col < 3; col++)
        {
            int piv = col;
            for (int r = col + 1; r < 3; r++) if (Math.Abs(a[r, col]) > Math.Abs(a[piv, col])) piv = r;
            if (Math.Abs(a[piv, col]) < 1e-12) return null;
            if (piv != col)
            {
                for (int c = 0; c < 3; c++) (a[col, c], a[piv, c]) = (a[piv, c], a[col, c]);
                (b[col], b[piv]) = (b[piv], b[col]);
            }
            for (int r = 0; r < 3; r++)
            {
                if (r == col) continue;
                double k = a[r, col] / a[col, col];
                for (int c = 0; c < 3; c++) a[r, c] -= k * a[col, c];
                b[r] -= k * b[col];
            }
        }
        return [b[0] / a[0, 0], b[1] / a[1, 1], b[2] / a[2, 2]];
    }

    static double EdgeDistance(IReadOnlyList<Vec2> poly, Vec2 p)
    {
        double d = double.PositiveInfinity;
        for (int i = 0; i < poly.Count; i++)
            d = Math.Min(d, Geom.SegmentDistance(p, poly[i], poly[(i + 1) % poly.Count]));
        return d;
    }
}
