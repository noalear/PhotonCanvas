using PhotonCanvas.Core.Imaging;

namespace PhotonCanvas.Addon.PhotoRelief;

/// <summary>A relief made: heights 0 (deepest) … 1 (the surface), NaN where nothing is carved (outside the plaque).</summary>
public sealed record ReliefResult(float[] Heights, int Cols, int Rows, double MmPerPx, double WidthMm, double HeightMm)
{
    /// <summary>How much of the plaque the subject covers (0–1).</summary>
    public double SubjectShare { get; init; } = 1;
    /// <summary>A plain background was asked for but none was found round the edges, so the whole photo was used.</summary>
    public bool BackgroundNotFound { get; init; }
    /// <summary>A plain background was asked for, but the edges are busy (many colours), so the subject found may be wrong.</summary>
    public bool BusyBackground { get; init; }
}

/// <summary>
/// Turns a photo into a relief. Brightness is a poor height on its own (dark hair carves as a pit, a
/// shadow as a cliff), so the photo is taken apart into detail sizes, from fine texture to the big
/// light and dark areas; each size's jumps are evened out (big ones flattened far more than small
/// ones) and weighted, and the sizes are added back up. The subject, found off a plain background, is
/// raised as a rounded, pillowed shape the detail rides on, the way a carver blocks in a portrait
/// before the features. The background becomes a flat field below it, and a plaque with a rim can go
/// round it all. No AI and nothing downloaded: it works on any photo, best on a plain background.
/// </summary>
public static class PhotoDepth
{
    /// <summary>The subject is found on a picture this many pixels along its longer side, whatever the relief's own size.</summary>
    public const int MaskSide = 480;

    /// <summary>The relief at <paramref name="cols"/> × <paramref name="rows"/> pixels covering the whole plaque.</summary>
    public static ReliefResult Make(ReliefSpec spec, ReliefPhoto photo, int cols, int rows, CancellationToken ct = default)
    {
        var (wMm, hMm) = spec.Size(photo.Width, photo.Height);
        cols = Math.Max(2, cols);
        rows = Math.Max(2, rows);
        int n = cols * rows;
        double sx = wMm / cols, sy = hMm / rows, mmpp = Math.Max(sx, sy);
        var crop = Crop(spec, photo, cols, rows);
        ct.ThrowIfCancellationRequested();

        // The plaque: how far each pixel is in from its edge (mm), and the part inside the rim.
        double rim = spec.HasPlaque ? Math.Max(0, spec.RimMm) : 0;
        var edge = new float[n];
        var inner = new float[n];
        Parallel.For(0, rows, r =>
        {
            for (int c = 0; c < cols; c++)
            {
                int i = r * cols + c;
                double e = spec.HasPlaque ? EdgeDistance(spec.Shape, wMm, hMm, spec.CornerMm, (c + 0.5) * sx, (r + 0.5) * sy) : double.PositiveInfinity;
                edge[i] = (float)e;
                inner[i] = (float)Math.Clamp((e - rim) / mmpp + 0.5, 0, 1);
            }
        });

        // The subject.
        var subject = new float[n];
        bool notFound = false, plain = true;
        var px = crop.Rgba;
        switch (spec.Subject)
        {
            case SubjectFrom.WholePicture:
                Array.Fill(subject, 1f);
                break;
            case SubjectFrom.SeeThrough:
                for (int i = 0; i < n; i++) subject[i] = px[i * 4 + 3] / 255f;
                break;
            default:
                // Found at one size whatever the detail asked for, so the preview and the depth map agree.
                double mk = MaskSide / (double)Math.Max(cols, rows);
                int mc = Math.Max(2, (int)Math.Round(cols * mk)), mr = Math.Max(2, (int)Math.Round(rows * mk));
                bool same = mc == cols && mr == rows;
                var small = same ? crop : Crop(spec, photo, mc, mr);
                var (wx, wy, ww, wh) = Window(spec, photo);
                var picks = spec.Picks.Select(p => (Colour: SubjectMask.ColourAt(photo.Image, p.U * photo.Width, p.V * photo.Height),
                    X: (int)Math.Floor((p.U * photo.Width - wx) / ww * mc), Y: (int)Math.Floor((p.V * photo.Height - wy) / wh * mr))).ToList();
                (var found, plain) = SubjectMask.Find(small, spec.Tolerance, Math.Max(wMm / mc, hMm / mr), picks);
                subject = same ? found : Filters.Resample(found, mc, mr, cols, rows);
                break;
        }
        if (spec.Subject != SubjectFrom.WholePicture)
        {
            double inside = 0, kept = 0;
            for (int i = 0; i < n; i++) { inside += inner[i]; kept += inner[i] * subject[i]; }
            double share = inside > 0 ? kept / inside : 0;
            // Nothing (or next to nothing) taken away, or everything: there's no background to go by.
            if (share > 0.995 || share < 0.02) { notFound = true; Array.Fill(subject, 1f); }
        }
        var s = new float[n];
        for (int i = 0; i < n; i++) s[i] = subject[i] * inner[i];
        ct.ThrowIfCancellationRequested();

        // Brightness, the background filled with the subject's average so it doesn't bleed into its edges.
        var lum = new float[n];
        double sum = 0, weight = 0;
        for (int i = 0; i < n; i++)
        {
            int p = i * 4;
            float y = (0.2126f * px[p] + 0.7152f * px[p + 1] + 0.0722f * px[p + 2]) / 255f;
            if (spec.DarkIsHigh) y = 1 - y;
            lum[i] = y;
            sum += y * s[i];
            weight += s[i];
        }
        float mean = weight > 0 ? (float)(sum / weight) : 0.5f;
        for (int i = 0; i < n; i++) lum[i] = s[i] * lum[i] + (1 - s[i]) * mean;

        if (spec.Smooth > 0.5)
        {
            double sm = Math.Clamp(spec.Smooth / 100, 0, 1);
            int r = Math.Max(1, (int)Math.Round((0.3 + 1.2 * sm) / mmpp));
            double eps = Math.Pow(0.02 + 0.1 * sm, 2);
            lum = Filters.Guided(lum, cols, rows, r, (float)eps);
        }
        ct.ThrowIfCancellationRequested();

        var detail = Detail(lum, s, cols, rows, mmpp, spec, ct);

        // How far each pixel is inside the subject (mm), for its rounding.
        var solid = new bool[n];
        for (int i = 0; i < n; i++) solid[i] = s[i] > 0.5f;
        var dist = Filters.DistanceInside(solid, cols, rows);
        ct.ThrowIfCancellationRequested();

        double raise = Math.Clamp(spec.Raise / 100, 0, 1), round = Math.Max(0, spec.RoundingMm);
        var shape = new float[n];
        for (int i = 0; i < n; i++)
        {
            if (round <= 0) { shape[i] = solid[i] ? 1 : 0; continue; }
            double t = Math.Clamp(dist[i] * mmpp / round, 0, 1);
            shape[i] = (float)Math.Sqrt(1 - (1 - t) * (1 - t));
        }
        if (round > 0) shape = Filters.Gaussian(shape, cols, rows, round / 6 / mmpp);
        // The detail fades out over the last mm to the subject's edge, so the outline isn't a cliff.
        double fadeMm = Math.Max(mmpp, 0.8);
        var rel = new float[n];
        double top = 0;
        for (int i = 0; i < n; i++)
        {
            double fade = Math.Clamp(dist[i] * mmpp / fadeMm, 0, 1);
            double v = (raise * shape[i] + (1 - raise) * detail[i] * fade) * s[i];
            rel[i] = (float)v;
            if (solid[i] && v > top) top = v;
        }
        if (top > 1e-6) for (int i = 0; i < n; i++) rel[i] = (float)Math.Min(1, rel[i] / top);

        // Put together: the field, the subject on it, the rim round it, nothing outside the plaque.
        // The whole photo has no background: it uses all the depth, and the rim falls to the bottom.
        bool whole = spec.Subject == SubjectFrom.WholePicture;
        bool sunk = spec.Background == BackgroundStyle.NotCarved && !whole;
        double field = sunk ? 1 : whole ? 0 : 1 - Math.Clamp(spec.BackgroundDepth / 100, 0, 1);
        var h = new float[n];
        double subjectArea = 0, plaqueArea = 0;
        for (int i = 0; i < n; i++)
        {
            double e = edge[i];
            if (spec.HasPlaque && e < 0) { h[i] = float.NaN; continue; }
            double si = s[i];
            double sub = sunk ? rel[i] : field + (1 - field) * rel[i];
            double v = si * sub + (1 - si) * field;
            if (rim > 0 && e < rim + mmpp)
            {
                double u = Math.Clamp(e / rim, 0, 1);
                double rimH = u < 0.5 ? 1 : field + (1 - field) * Math.Sqrt(Math.Max(0, 1 - Math.Pow((u - 0.5) / 0.5, 2)));
                v = inner[i] * v + (1 - inner[i]) * rimH;
            }
            h[i] = (float)Math.Clamp(v, 0, 1);
            plaqueArea++;
            subjectArea += si;
        }
        return new ReliefResult(h, cols, rows, mmpp, wMm, hMm)
        {
            SubjectShare = plaqueArea > 0 ? subjectArea / plaqueArea : 0,
            BackgroundNotFound = notFound,
            BusyBackground = !plain && !notFound,
        };
    }

    /// <summary>
    /// The photo's shading as heights 0–1: split into sizes (0.3 mm, 0.6, 1.2 … up to a quarter of
    /// the picture), each size's jumps evened out and weighted (fine ones by Detail, big ones by Form),
    /// then added back up and spread over 0–1 across the subject.
    /// </summary>
    static float[] Detail(float[] lum, float[] subject, int cols, int rows, double mmpp, ReliefSpec spec, CancellationToken ct)
    {
        int n = cols * rows;
        double even = Math.Clamp(spec.EvenOut / 100, 0, 1);
        double wFine = 2 * Math.Clamp(spec.Detail / 100, 0, 1), wBroad = 2 * Math.Clamp(spec.Form / 100, 0, 1);
        var h = new double[n];
        var prev = lum;
        double largest = Math.Max(cols, rows) * mmpp / 4;
        for (double sigma = 0.3; sigma <= largest; sigma *= 2)
        {
            ct.ThrowIfCancellationRequested();
            if (sigma / mmpp < 0.35) continue; // finer than a pixel: nothing there
            var g = Filters.Gaussian(lum, cols, rows, sigma / mmpp);
            double w = sigma < 1.2 ? wFine : sigma < 6 ? 1 : wBroad;
            var band = new float[n];
            for (int i = 0; i < n; i++) band[i] = prev[i] - g[i];
            AddEvened(h, band, subject, w, even, 0);
            prev = g;
        }
        // What's left: the broadest light and dark.
        double m = 0, k = 0;
        for (int i = 0; i < n; i++) { m += prev[i] * subject[i]; k += subject[i]; }
        AddEvened(h, prev, subject, wBroad, even, k > 0 ? m / k : 0.5);

        var (lo, hi) = Percentiles(h, subject, 0.005, 0.995);
        var o = new float[n];
        double span = hi - lo;
        if (span < 1e-9) return o;
        for (int i = 0; i < n; i++) o[i] = (float)Math.Clamp((h[i] - lo) / span, 0, 1);
        return o;
    }

    /// <summary>Adds one size of detail: big jumps squashed (a soft limit a few times its usual size), then weighted.</summary>
    static void AddEvened(double[] h, float[] band, float[] subject, double weight, double even, double centre)
    {
        if (weight <= 0) return;
        if (even < 0.005)
        {
            for (int i = 0; i < h.Length; i++) h[i] += weight * (band[i] - centre);
            return;
        }
        double sd = Spread(band, subject, centre) + 1e-6;
        double t = sd * (3 - 2.6 * even);
        for (int i = 0; i < h.Length; i++) h[i] += weight * t * Math.Tanh((band[i] - centre) / t);
    }

    /// <summary>The typical size of a band's values over the subject (root mean square about <paramref name="centre"/>).</summary>
    static double Spread(float[] v, float[] weight, double centre)
    {
        int step = Math.Max(1, v.Length / 250_000);
        double s = 0, w = 0;
        for (int i = 0; i < v.Length; i += step)
        {
            double d = v[i] - centre;
            s += d * d * weight[i];
            w += weight[i];
        }
        return w > 0 ? Math.Sqrt(s / w) : 0;
    }

    static (double Lo, double Hi) Percentiles(double[] v, float[] weight, double lo, double hi)
    {
        int step = Math.Max(1, v.Length / 250_000);
        var list = new List<double>(v.Length / step + 1);
        for (int i = 0; i < v.Length; i += step) if (weight[i] > 0.5f) list.Add(v[i]);
        if (list.Count == 0) for (int i = 0; i < v.Length; i += step) list.Add(v[i]);
        if (list.Count == 0) return (0, 0);
        list.Sort();
        return (list[(int)Math.Round(lo * (list.Count - 1))], list[(int)Math.Round(hi * (list.Count - 1))]);
    }

    /// <summary>How far (mm) the point is inside the plaque's outline (negative outside).</summary>
    public static double EdgeDistance(PlaqueShape shape, double w, double h, double cornerMm, double x, double y)
    {
        double rect = Math.Min(Math.Min(x, w - x), Math.Min(y, h - y));
        switch (shape)
        {
            case PlaqueShape.Rounded:
                {
                    double r = Math.Clamp(cornerMm, 0, Math.Min(w, h) / 2);
                    double qx = Math.Abs(x - w / 2) - (w / 2 - r), qy = Math.Abs(y - h / 2) - (h / 2 - r);
                    if (qx > 0 && qy > 0) return r - Math.Sqrt(qx * qx + qy * qy);
                    return rect;
                }
            case PlaqueShape.Oval:
            case PlaqueShape.Circle:
                {
                    double a = w / 2, b = h / 2, px = x - a, py = y - b;
                    if (Math.Abs(a - b) < 1e-9) return a - Math.Sqrt(px * px + py * py);
                    // A close estimate of the distance to an ellipse (exact on its axes and its outline).
                    double k0 = Math.Sqrt(px * px / (a * a) + py * py / (b * b));
                    double k1 = Math.Sqrt(px * px / (a * a * a * a) + py * py / (b * b * b * b));
                    if (k1 < 1e-12) return Math.Min(a, b);
                    return -k0 * (k0 - 1) / k1;
                }
            default:
                return rect;
        }
    }

    /// <summary>The part of the photo on the plaque, in photo pixels: its top-left and size.</summary>
    public static (double X, double Y, double W, double H) Window(ReliefSpec spec, ReliefPhoto photo)
    {
        var (wMm, hMm) = spec.Size(photo.Width, photo.Height);
        int pw = photo.Width, ph = photo.Height;
        // mm per photo pixel when the photo just covers the plaque, then zoomed in.
        double s = Math.Max(wMm / pw, hMm / ph) * Math.Clamp(spec.Zoom, 1, 8);
        double ww = Math.Min(pw, wMm / s), wh = Math.Min(ph, hMm / s);
        return ((pw - ww) / 2 * (1 + Math.Clamp(spec.PanX, -1, 1)), (ph - wh) / 2 * (1 + Math.Clamp(spec.PanY, -1, 1)), ww, wh);
    }

    /// <summary>The part of the photo that shows on the plaque, at <paramref name="cols"/> × <paramref name="rows"/> pixels.</summary>
    public static RasterImage Crop(ReliefSpec spec, ReliefPhoto photo, int cols, int rows)
    {
        var (ox, oy, ww, wh) = Window(spec, photo);
        double step = Math.Min(ww / cols, wh / rows); // photo pixels per output pixel
        int f = Math.Max(1, (int)Math.Floor(step / 1.5 + 1e-9));
        if (step < 1.5) f = 1;
        var src = photo.At(f);
        int w = src.Width, h = src.Height;
        var sp = src.Rgba;
        var o = new byte[cols * rows * 4];
        double kx = ww / cols, ky = wh / rows;
        Parallel.For(0, rows, r =>
        {
            for (int c = 0; c < cols; c++)
            {
                // Photo pixel coordinates (pixel centres at i + 0.5), then in the smaller copy.
                double u = (ox + (c + 0.5) * kx) / f - 0.5, v = (oy + (r + 0.5) * ky) / f - 0.5;
                u = Math.Clamp(u, 0, w - 1);
                v = Math.Clamp(v, 0, h - 1);
                int x0 = (int)Math.Floor(u), y0 = (int)Math.Floor(v);
                int x1 = Math.Min(w - 1, x0 + 1), y1 = Math.Min(h - 1, y0 + 1);
                double fx = u - x0, fy = v - y0;
                int q = (r * cols + c) * 4;
                for (int ch = 0; ch < 4; ch++)
                {
                    double a = sp[(y0 * w + x0) * 4 + ch] * (1 - fx) + sp[(y0 * w + x1) * 4 + ch] * fx;
                    double b = sp[(y1 * w + x0) * 4 + ch] * (1 - fx) + sp[(y1 * w + x1) * 4 + ch] * fx;
                    o[q + ch] = (byte)Math.Round(a * (1 - fy) + b * fy);
                }
            }
        });
        return new RasterImage(cols, rows, o);
    }

    /// <summary>The depth map: white the surface, black the deepest, see-through where nothing is carved.</summary>
    public static RasterImage DepthMap(ReliefResult r)
    {
        var o = new byte[r.Cols * r.Rows * 4];
        for (int i = 0; i < r.Heights.Length; i++)
        {
            float v = r.Heights[i];
            int q = i * 4;
            if (float.IsNaN(v)) { o[q] = o[q + 1] = o[q + 2] = 255; o[q + 3] = 0; continue; }
            byte g = (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);
            o[q] = o[q + 1] = o[q + 2] = g;
            o[q + 3] = 255;
        }
        return new RasterImage(r.Cols, r.Rows, o);
    }
}
