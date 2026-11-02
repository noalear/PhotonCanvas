using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;

namespace PhotonCanvas.Doc.Colour;

/// <summary>How a picture becomes diffraction-grating pixels.</summary>
public enum DiffractionMode
{
    /// <summary>Brightness sets each pixel's grating direction: the picture shimmers and changes as it's tilted.</summary>
    Shimmer,
    /// <summary>Hue sets each pixel's grating pitch: the colours show at one particular lighting and viewing angle.</summary>
    Colour,
    /// <summary>
    /// A depth map (light = in front, mid-grey = on the surface, dark = behind, white = nothing)
    /// as an "almost-hologram": each outline point becomes an arc of grating pixels, as wide as
    /// the point is deep, so its glint slides along the arc as the plate tilts and the point
    /// seems to float in front of or behind the metal. Experimental.
    /// </summary>
    Depth,
}

/// <summary>
/// Settings for a diffraction picture. The grating is the spacing of the laser's pulses along
/// each line (pitch = speed ÷ frequency: 300 mm/s at 300 kHz = 1 µm), so its ridges run across
/// the direction of travel; the fill's scan direction sets which way a pixel flashes.
/// </summary>
public sealed record DiffractionOptions
{
    public DiffractionMode Mode { get; init; } = DiffractionMode.Shimmer;
    /// <summary>Size of one grating pixel, mm.</summary>
    public double PixelMm { get; init; } = 0.5;
    /// <summary>Shimmer: how many directions brightness is shared out over (0–180°).</summary>
    public int AngleSteps { get; init; } = 12;
    /// <summary>Colour: how many pitches hues are shared out over.</summary>
    public int ColourSteps { get; init; } = 10;
    /// <summary>Colour: the scan direction for every pixel, degrees.</summary>
    public double ColourAngleDeg { get; init; }
    /// <summary>Grating pitch that shows green (530 nm) where you'll view it from, µm. Shimmer uses it for every pixel.</summary>
    public double GreenPitchUm { get; init; } = 1.0;
    /// <summary>Leave near-white pixels unmarked (bare metal).</summary>
    public bool SkipLight { get; init; } = true;
    /// <summary>Colour: leave greys and near-black unmarked (a grating can't show them).</summary>
    public bool SkipGrey { get; init; } = true;
    /// <summary>Depth: arc radius for the nearest and farthest points, mm (the strength of the 3D).</summary>
    public double DepthMm { get; init; } = 6;
    /// <summary>Depth: distance between the points that get arcs, mm.</summary>
    public double DepthPointMm { get; init; } = 2;
    /// <summary>Depth: only outlines and sudden depth changes get arcs (for line art and stepped shapes); off = points all over (smooth reliefs).</summary>
    public bool DepthOutlines { get; init; }
    /// <summary>Depth: swap in front and behind (if it looks inside out under your light).</summary>
    public bool DepthFlip { get; init; }

    // The marking (what has worked on polished stainless: 60 ns, 300 mm/s, lines 0.06 mm apart, low power).
    public double Power { get; init; } = 32;
    public double Speed { get; init; } = 300;
    public int PulseWidthNs { get; init; } = 60;
    public double LineSpacingMm { get; init; } = 0.06;
    public double OverscanMm { get; init; } = 0.2;
}

/// <summary>One group of pixels marked alike: its scan direction and pitch, and how many pixels.</summary>
public sealed record GratingBucket(int Index, double AngleDeg, double PitchUm, int FrequencyKhz, int Pixels, uint Preview);

/// <summary>A picture worked out as grating pixels: which bucket each pixel is in (−1 = unmarked).</summary>
public sealed record GratingMap(int Cols, int Rows, int[] Cells, IReadOnlyList<GratingBucket> Buckets, double PixelMm);

/// <summary>Turns a picture into diffraction-grating pixels, one fill layer per direction or pitch.</summary>
public static class Diffraction
{
    public const string LayerPrefix = "Grating ";

    /// <summary>Pulse spacing along a line, µm: speed (mm/s) ÷ frequency (kHz).</summary>
    public static double PitchUm(double speed, int khz) => speed / Math.Max(1, khz);

    /// <summary>Roughly the wavelength (nm) an HSV hue shows as: red 640 → violet 420 (purples count as the nearer end).</summary>
    public static double Wavelength(double hueDeg)
    {
        double h = (hueDeg % 360 + 360) % 360;
        (double Hue, double Nm)[] stops = [(0, 640), (30, 605), (60, 578), (120, 530), (180, 490), (240, 460), (275, 420)];
        if (h >= 275) return h < 318 ? 420 : 640; // purple/magenta: no single wavelength, take the nearer end
        for (int i = 1; i < stops.Length; i++)
            if (h <= stops[i].Hue)
            {
                double t = (h - stops[i - 1].Hue) / (stops[i].Hue - stops[i - 1].Hue);
                return stops[i - 1].Nm + t * (stops[i].Nm - stops[i - 1].Nm);
            }
        return 420;
    }

    /// <summary>Work out the grating pixels for a picture of <paramref name="sizeMm"/>.</summary>
    public static GratingMap Map(RasterImage img, Vec2 sizeMm, DiffractionOptions o, MachineProfile? machine = null)
    {
        var m = machine ?? MachineProfile.F2Ultra;
        var lim = m.MopaLimitFor(o.PulseWidthNs) ?? m.Mopa.MinBy(x => Math.Abs(x.PulseWidthNs - o.PulseWidthNs));
        double px = Math.Max(0.05, o.PixelMm);
        int cols = Math.Max(1, (int)Math.Round(sizeMm.X / px)), rows = Math.Max(1, (int)Math.Round(sizeMm.Y / px));
        var cells = new int[cols * rows];
        var keys = new Dictionary<int, int>(); // step → bucket
        var buckets = new List<(double Angle, double Pitch, int Khz, int Count, Lab Sum, int N)>();
        int Bucket(int step, double angle, double pitchUm, Lab look)
        {
            if (!keys.TryGetValue(step, out int b))
            {
                int khz = lim.Safe((int)Math.Round(o.Speed / pitchUm));
                b = buckets.Count;
                keys[step] = b;
                buckets.Add((angle, PitchUm(o.Speed, khz), khz, 0, default, 0));
            }
            var e = buckets[b];
            buckets[b] = e with { Count = e.Count + 1, Sum = new(e.Sum.L + look.L, e.Sum.A + look.A, e.Sum.B + look.B), N = e.N + 1 };
            return b;
        }

        int angleSteps = Math.Clamp(o.AngleSteps, 2, 72), colourSteps = Math.Clamp(o.ColourSteps, 2, 32);
        if (o.Mode == DiffractionMode.Depth)
        {
            DepthArcs(img, cols, rows, px, o, angleSteps, cells, (step, angle) => Bucket(step, angle, o.GreenPitchUm, new Lab(50, 0, 0)));
            goto done;
        }
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                var lab = Average(img, (double)c / cols, (double)r / rows, (double)(c + 1) / cols, (double)(r + 1) / rows, out bool transparent);
                int cell = -1;
                if (!transparent && !(o.SkipLight && lab.L > 92 && lab.Chroma < 8))
                {
                    if (o.Mode == DiffractionMode.Shimmer)
                    {
                        // Dark → 0°, light → 180°, in steps.
                        int step = (int)Math.Min(angleSteps - 1, Math.Floor(lab.L / 100.0 * angleSteps));
                        cell = Bucket(step, 180.0 * step / angleSteps, o.GreenPitchUm, lab);
                    }
                    else if (!(o.SkipGrey && lab.Chroma < 15))
                    {
                        var (sr, sg, sb) = ColourScience.LabToSrgb(lab);
                        double nm = Wavelength(HsvHue(sr, sg, sb));
                        int step = (int)Math.Round((nm - 420) / (640 - 420) * (colourSteps - 1));
                        double stepNm = 420 + (640 - 420) * step / (double)(colourSteps - 1);
                        cell = Bucket(step, o.ColourAngleDeg, o.GreenPitchUm * stepNm / 530, lab);
                    }
                }
                cells[r * cols + c] = cell;
            }
        done:
        var list = buckets.Select((b, i) =>
        {
            var mean = new Lab(b.Sum.L / b.N, b.Sum.A / b.N, b.Sum.B / b.N);
            var (rr, gg, bb) = ColourScience.LabToSrgb(mean);
            return new GratingBucket(i, b.Angle, b.Pitch, b.Khz, b.Count, (uint)(rr << 16 | gg << 8 | bb));
        }).ToList();
        return new(cols, rows, cells, list, px);
    }

    /// <summary>
    /// The depth map as arcs of grating pixels (abrasion-hologram style). A point at depth z
    /// (−1 behind … +1 in front) becomes a half circle of radius |z| × <see cref="DiffractionOptions.DepthMm"/>
    /// below it (in front) or above it (behind); each arc pixel's grating points away from the
    /// centre, so the glint travels round the arc as the plate tilts. Where arcs cross, the
    /// nearer point wins.
    /// </summary>
    static void DepthArcs(RasterImage img, int cols, int rows, double px, DiffractionOptions o, int angleSteps, int[] cells, Func<int, double, int> bucket)
    {
        // Depth per pixel: grey 0–255 → −1…+1 (white is "nothing here").
        var depth = new double?[cols * rows];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                var grey = DepthGrey(img, (double)c / cols, (double)r / rows, (double)(c + 1) / cols, (double)(r + 1) / rows);
                depth[r * cols + c] = grey is not { } g ? null : Math.Clamp((g - 127.5) / 127.5, -1, 1) * (o.DepthFlip ? -1 : 1);
            }
        Array.Fill(cells, -1);
        var nearest = new double[cols * rows];
        Array.Fill(nearest, double.NegativeInfinity);
        int every = Math.Max(1, (int)Math.Round(o.DepthPointMm / px));
        double maxR = Math.Max(0.1, o.DepthMm) / px; // in pixels
        bool Edge(int c, int r)
        {
            var z = depth[r * cols + c]!.Value;
            foreach (var (dc, dr) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int cc = c + dc, rr = r + dr;
                if (cc < 0 || rr < 0 || cc >= cols || rr >= rows) return true;
                if (depth[rr * cols + cc] is not { } z2 || Math.Abs(z2 - z) > 0.25) return true; // the outline, or a real jump in depth
            }
            return false;
        }
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                if (depth[r * cols + c] is not { } z) continue;
                // Points on a lattice (outlines are kept wherever they fall, thinned along the lattice too).
                bool onLattice = r % every == 0 && c % every == 0;
                if (o.DepthOutlines ? !(Edge(c, r) && (r % every == 0 || c % every == 0)) : !onLattice) continue;
                double rad = Math.Abs(z) * maxR;
                int sign = z >= 0 ? 1 : -1; // in front: arc below the point; behind: above
                if (rad < 0.75)
                {
                    // On the surface: no movement; a plain grating straight across.
                    Set(c, r, 0, z);
                    continue;
                }
                int n = Math.Max(8, (int)Math.Ceiling(Math.PI * rad * 1.5));
                for (int k = 0; k <= n; k++)
                {
                    double phi = Math.PI * k / n; // 0…180°
                    int pc = (int)Math.Round(c + rad * Math.Cos(phi)), pr = (int)Math.Round(r + sign * rad * Math.Sin(phi));
                    Set(pc, pr, phi * 180 / Math.PI, z);
                }
            }

        void Set(int c, int r, double angleDeg, double z)
        {
            if (c < 0 || r < 0 || c >= cols || r >= rows) return;
            int i = r * cols + c;
            if (z < nearest[i]) return;
            nearest[i] = z;
            // The grating points away from the arc's centre: its line direction, 0–180°.
            double a = (angleDeg % 180 + 180) % 180;
            int step = (int)Math.Round(a / 180 * angleSteps) % angleSteps;
            cells[i] = bucket(step, 180.0 * step / angleSteps);
        }
    }

    /// <summary>
    /// A depth map's grey (0–255) over a region, leaving out "nothing here" pixels (white or
    /// transparent): averaging them in would make an object's outline look like it stands out.
    /// Null when most of the region is nothing.
    /// </summary>
    static double? DepthGrey(RasterImage img, double fx0, double fy0, double fx1, double fy1)
    {
        int x0 = (int)Math.Floor(fx0 * img.Width), x1 = Math.Min(img.Width, Math.Max((int)Math.Floor(fx0 * img.Width) + 1, (int)Math.Ceiling(fx1 * img.Width)));
        int y0 = (int)Math.Floor(fy0 * img.Height), y1 = Math.Min(img.Height, Math.Max((int)Math.Floor(fy0 * img.Height) + 1, (int)Math.Ceiling(fy1 * img.Height)));
        int stepX = Math.Max(1, (x1 - x0) / 8), stepY = Math.Max(1, (y1 - y0) / 8);
        double sum = 0;
        int n = 0, nothing = 0;
        for (int y = y0; y < y1; y += stepY)
            for (int x = x0; x < x1; x += stepX)
            {
                int i = (y * img.Width + x) * 4;
                double g = 0.2126 * img.Rgba[i] + 0.7152 * img.Rgba[i + 1] + 0.0722 * img.Rgba[i + 2];
                if (img.Rgba[i + 3] < 128 || g >= 250) { nothing++; continue; }
                sum += g;
                n++;
            }
        return n == 0 || nothing > n ? null : sum / n;
    }

    /// <summary>
    /// Add the picture's grating layers over it: same place, size and angle. One fill layer per
    /// bucket ("Grating 45° · 1.00 µm"), its pixels merged into strips. Returns the new state and the shapes added.
    /// </summary>
    public static (DocState State, IReadOnlyList<Guid> Added) Add(DocState s, ImageShape picture, DiffractionOptions o, MachineProfile? machine = null)
    {
        var m = machine ?? MachineProfile.F2Ultra;
        var map = Map(picture.Source.Pixels, picture.SizeMm, o, m);
        var lim = m.MopaLimitFor(o.PulseWidthNs) ?? m.Mopa.MinBy(x => Math.Abs(x.PulseWidthNs - o.PulseWidthNs));
        double cw = picture.SizeMm.X / map.Cols, ch = picture.SizeMm.Y / map.Rows;
        var toWorld = picture.Transform; // picture-local mm → world
        var added = new List<Guid>();
        int layerIndex = s.Layers.Count;
        foreach (var b in map.Buckets.Where(b => b.Pixels > 0).OrderBy(b => b.PitchUm).ThenBy(b => b.AngleDeg))
        {
            var subpaths = PixelGrid.Shape(map.Cols, map.Rows, map.Cells, b.Index, cw, ch, toWorld);
            var layer = Layer.Default(layerIndex, LayerMode.Fill) with
            {
                Name = $"{LayerPrefix}{b.AngleDeg:0.#}° · {b.PitchUm:0.00} µm",
                Color = b.Preview,
                Laser = new LaserSettings { Light = LightSource.Red, Power = Math.Clamp(o.Power, 0, 100), Speed = Math.Clamp(o.Speed, m.MinSpeed, m.MaxSpeed), PulseWidthNs = lim.PulseWidthNs, FrequencyKhz = b.FrequencyKhz },
                LinesPerCm = Math.Clamp(10 / Math.Max(1e-3, o.LineSpacingMm), 1, FillBlock.MaxLinesPerCm),
                // The fill angle is measured on screen; the picture's own turn comes on top.
                FillAngleDeg = b.AngleDeg + picture.Transform.RotationDeg,
                Crosshatch = false,
                OverscanMm = Math.Clamp(o.OverscanMm, 0, 5),
            };
            layerIndex++;
            s = Ops.AddLayer(s, layer);
            var shape = new PathShape { Name = layer.Name, LayerId = layer.Id, Geometry = new PathGeometry(subpaths) };
            s = Ops.AddShapes(s, [shape]);
            added.Add(shape.Id);
        }
        return (s, added);
    }

    /// <summary>HSV hue, degrees (0 red, 120 green, 240 blue): the colour-wheel hue <see cref="Wavelength"/> expects.</summary>
    public static double HsvHue(byte r, byte g, byte b)
    {
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        if (d <= 0) return 0;
        double h = max == r ? (g - b) / d % 6 : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        return (h * 60 + 360) % 360;
    }

    /// <summary>Average colour of a region of the picture (fractions 0–1), as Lab. Mostly transparent regions count as transparent.</summary>
    public static Lab Average(RasterImage img, double fx0, double fy0, double fx1, double fy1, out bool transparent)
    {
        int x0 = (int)Math.Floor(fx0 * img.Width), x1 = Math.Max(x0 + 1, (int)Math.Ceiling(fx1 * img.Width));
        int y0 = (int)Math.Floor(fy0 * img.Height), y1 = Math.Max(y0 + 1, (int)Math.Ceiling(fy1 * img.Height));
        x1 = Math.Min(x1, img.Width);
        y1 = Math.Min(y1, img.Height);
        double r = 0, g = 0, b = 0;
        int n = 0, clear = 0;
        // Big regions are sampled on a grid rather than every pixel.
        int stepX = Math.Max(1, (x1 - x0) / 8), stepY = Math.Max(1, (y1 - y0) / 8);
        for (int y = y0; y < y1; y += stepY)
            for (int x = x0; x < x1; x += stepX)
            {
                int i = (y * img.Width + x) * 4;
                if (img.Rgba[i + 3] < 128) { clear++; continue; }
                r += ColourScience.ToLinear(img.Rgba[i] / 255.0);
                g += ColourScience.ToLinear(img.Rgba[i + 1] / 255.0);
                b += ColourScience.ToLinear(img.Rgba[i + 2] / 255.0);
                n++;
            }
        transparent = n == 0 || clear > n;
        return n == 0 ? new(100, 0, 0) : ColourScience.LinearRgbToLab(r / n, g / n, b / n);
    }
}

/// <summary>Pixel grids made into shapes: each value's pixels as strips (runs along rows, merged down).</summary>
public static class PixelGrid
{
    /// <summary>The pixels holding <paramref name="value"/>, as rectangles of whole pixels (columns c0–c1, rows r0 to r1−1).</summary>
    public static List<(int C0, int C1, int R0, int R1)> Strips(int cols, int rows, int[] cells, int value)
    {
        var rects = new List<(int C0, int C1, int R0, int R1)>();
        var open = new Dictionary<(int, int), int>(); // (c0, c1) → the strip still growing down
        for (int r = 0; r < rows; r++)
        {
            var next = new Dictionary<(int, int), int>();
            for (int c = 0; c < cols; c++)
            {
                if (cells[r * cols + c] != value) continue;
                int c0 = c;
                while (c + 1 < cols && cells[r * cols + c + 1] == value) c++;
                if (open.TryGetValue((c0, c), out int k) && rects[k].R1 == r)
                {
                    rects[k] = rects[k] with { R1 = r + 1 };
                    next[(c0, c)] = k;
                }
                else
                {
                    rects.Add((c0, c, r, r + 1));
                    next[(c0, c)] = rects.Count - 1;
                }
            }
            open = next;
        }
        return rects;
    }

    /// <summary>Those strips as closed outlines, pixel size <paramref name="cw"/> × <paramref name="ch"/> mm, placed by <paramref name="toWorld"/>.</summary>
    public static List<Subpath> Shape(int cols, int rows, int[] cells, int value, double cw, double ch, Affine toWorld) =>
        Strips(cols, rows, cells, value).SelectMany(q =>
        {
            double x0 = q.C0 * cw, x1 = (q.C1 + 1) * cw, y0 = q.R0 * ch, y1 = q.R1 * ch;
            return PathGeometry.Rect(x0, y0, x1 - x0, y1 - y0).Transform(toWorld).Subpaths;
        }).ToList();
}
