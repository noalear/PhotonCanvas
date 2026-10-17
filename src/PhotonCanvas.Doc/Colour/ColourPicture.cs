using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;

namespace PhotonCanvas.Doc.Colour;

/// <summary>How a picture is turned into the colours a palette measured.</summary>
public sealed record ColourPictureOptions
{
    /// <summary>One colour pixel, mm. Smaller = finer, but neighbouring colours bleed into each other with heat.</summary>
    public double PixelMm { get; init; } = 0.3;
    /// <summary>Squeeze the picture's lightness into the range the palette really has (so shadows and highlights both land somewhere).</summary>
    public bool FitLightness { get; init; } = true;
    /// <summary>Mix colours with dots (error diffusion) for in-between shades; off = nearest colour only (posterised).</summary>
    public bool Dither { get; init; } = true;
    /// <summary>Palette colours closer than this (ΔE00) count as one: fewer layers, fewer near-duplicates.</summary>
    public double MinDeltaE { get; init; } = 5;
    /// <summary>Unmarked metal is one of the colours (usually the lightest).</summary>
    public bool UseBare { get; init; } = true;
    /// <summary>Run-up and run-out with the laser off past each line, mm (steadier colour at the edges).</summary>
    public double OverscanMm { get; init; }
}

/// <summary>A colour a picture can use: a measured square (its settings make it), or bare metal.</summary>
public sealed record PictureInk(int Index, Lab Colour, ColourSwatch? Swatch)
{
    public bool IsBare => Swatch is null;
}

/// <summary>A picture worked out in palette colours: which ink each pixel gets (−1 = outside the picture).</summary>
public sealed record InkMap(int Cols, int Rows, int[] Cells, IReadOnlyList<PictureInk> Inks, IReadOnlyList<int> Counts, double PixelMm);

/// <summary>
/// Colour pictures on metal from a measured palette: every pixel becomes the nearest colour the
/// plate really made (mixing neighbouring colours with dots for the shades in between), and each
/// colour becomes one fill layer with the settings that made it, least energy first.
/// </summary>
public static class ColourPicture
{
    public const string LayerPrefix = "Colour · ";

    /// <summary>The colours to use: the clearly different, even squares, plus bare metal.</summary>
    public static IReadOnlyList<PictureInk> Inks(ColourPalette palette, ColourPictureOptions o)
    {
        var inks = new List<PictureInk>();
        if (o.UseBare) inks.Add(new(0, palette.Bare, null));
        foreach (var s in ColourMeasure.Distinct(palette.Swatches, o.MinDeltaE))
        {
            // Bare metal is already in: skip squares that look just like it.
            if (o.UseBare && ColourScience.DeltaE2000(s.Colour, palette.Bare) < o.MinDeltaE) continue;
            inks.Add(new(inks.Count, s.Colour, s));
        }
        return inks;
    }

    public static InkMap Map(RasterImage img, Vec2 sizeMm, ColourPalette palette, ColourPictureOptions o)
    {
        var inks = Inks(palette, o);
        double px = Math.Max(0.05, o.PixelMm);
        int cols = Math.Max(1, (int)Math.Round(sizeMm.X / px)), rows = Math.Max(1, (int)Math.Round(sizeMm.Y / px));
        var lab = new Lab[cols * rows];
        var clear = new bool[cols * rows];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                lab[r * cols + c] = Diffraction.Average(img, (double)c / cols, (double)r / rows, (double)(c + 1) / cols, (double)(r + 1) / rows, out bool t);
                clear[r * cols + c] = t;
            }
        var cells = new int[cols * rows];
        var counts = new int[inks.Count];
        if (inks.Count == 0)
        {
            Array.Fill(cells, -1);
            return new(cols, rows, cells, inks, counts, px);
        }

        if (o.FitLightness)
        {
            // The picture's 1st–99th percentile lightness onto the palette's range.
            var ls = lab.Where((_, i) => !clear[i]).Select(v => v.L).OrderBy(v => v).ToList();
            if (ls.Count > 1)
            {
                double lo = ls[(int)(ls.Count * 0.01)], hi = ls[(int)((ls.Count - 1) * 0.99)];
                double pLo = inks.Min(k => k.Colour.L), pHi = inks.Max(k => k.Colour.L);
                if (hi - lo > 1)
                    for (int i = 0; i < lab.Length; i++)
                        lab[i] = lab[i] with { L = pLo + Math.Clamp((lab[i].L - lo) / (hi - lo), 0, 1) * (pHi - pLo) };
            }
        }

        int Nearest(Lab v)
        {
            int best = 0;
            double bd = double.MaxValue;
            for (int k = 0; k < inks.Count; k++)
            {
                var c = inks[k].Colour;
                double d = (v.L - c.L) * (v.L - c.L) + (v.A - c.A) * (v.A - c.A) + (v.B - c.B) * (v.B - c.B);
                if (d < bd) { bd = d; best = k; }
            }
            return best;
        }

        // Floyd–Steinberg in CIELAB, back and forth along the rows; the error is limited so one
        // colour the palette can't reach doesn't smear across the picture.
        var work = (Lab[])lab.Clone();
        for (int r = 0; r < rows; r++)
        {
            bool ltr = r % 2 == 0;
            for (int k = 0; k < cols; k++)
            {
                int c = ltr ? k : cols - 1 - k, i = r * cols + c;
                if (clear[i]) { cells[i] = -1; continue; }
                int ink = Nearest(work[i]);
                cells[i] = ink;
                counts[ink]++;
                if (!o.Dither) continue;
                var want = work[i];
                var got = inks[ink].Colour;
                double eL = Math.Clamp(want.L - got.L, -40, 40), eA = Math.Clamp(want.A - got.A, -40, 40), eB = Math.Clamp(want.B - got.B, -40, 40);
                void Push(int cc, int rr, double w)
                {
                    if (cc < 0 || cc >= cols || rr >= rows) return;
                    int j = rr * cols + cc;
                    if (clear[j]) return;
                    work[j] = new(work[j].L + eL * w, work[j].A + eA * w, work[j].B + eB * w);
                }
                int dir = ltr ? 1 : -1;
                Push(c + dir, r, 7 / 16.0);
                Push(c - dir, r + 1, 3 / 16.0);
                Push(c, r + 1, 5 / 16.0);
                Push(c + dir, r + 1, 1 / 16.0);
            }
        }
        return new(cols, rows, cells, inks, counts, px);
    }

    /// <summary>
    /// Add the picture's colour layers over it: same place, size and angle; one fill layer per
    /// colour used (bare metal needs none), with the settings that made it, least energy first.
    /// </summary>
    public static (DocState State, IReadOnlyList<Guid> Added) Add(DocState s, ImageShape picture, ColourPalette palette, ColourPictureOptions o)
    {
        var map = Map(picture.Source.Pixels, picture.SizeMm, palette, o);
        double cw = picture.SizeMm.X / map.Cols, ch = picture.SizeMm.Y / map.Rows;
        var added = new List<Guid>();
        int layerIndex = s.Layers.Count;
        foreach (var ink in map.Inks.Where(k => !k.IsBare && map.Counts[k.Index] > 0)
                     .OrderBy(k => ColourTest.Energy(k.Swatch!.Settings.Laser, k.Swatch.Settings.LinesPerCm)))
        {
            var sw = ink.Swatch!;
            var (r, g, b) = ColourScience.LabToSrgb(ink.Colour);
            var layer = sw.Settings.ApplyTo(Layer.Default(layerIndex++, LayerMode.Fill)) with
            {
                Name = $"{LayerPrefix}#{r:X2}{g:X2}{b:X2} (R{sw.Row + 1} C{sw.Col + 1})",
                Color = (uint)(r << 16 | g << 8 | b),
                Mode = LayerMode.Fill,
                OverscanMm = Math.Clamp(o.OverscanMm, 0, 5),
            };
            s = Ops.AddLayer(s, layer);
            var shape = new PathShape { Name = layer.Name, LayerId = layer.Id, Geometry = new PathGeometry(PixelGrid.Shape(map.Cols, map.Rows, map.Cells, ink.Index, cw, ch, picture.Transform)) };
            s = Ops.AddShapes(s, [shape]);
            added.Add(shape.Id);
        }
        return (s, added);
    }
}
