using System.Collections.Immutable;
using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Colour;
using PhotonCanvas.Doc.Materials;

namespace PhotonCanvas.Addon.Flip;

/// <summary>How the pictures' grating patches are woven together on the plate.</summary>
public enum FlipWeave
{
    /// <summary>Thin columns, one picture after another (left to right).</summary>
    Columns,
    /// <summary>Thin rows, one picture after another (top to bottom).</summary>
    Rows,
    /// <summary>A checkerboard-like tile (2 pictures: checkerboard; 4: a 2 × 2 tile).</summary>
    Tiles,
    /// <summary>Diagonal stripes.</summary>
    Diagonal,
}

/// <summary>
/// One of the pictures in a flip picture: the grating it is marked with (scan direction and
/// pitch) and which of its parts are marked.
/// </summary>
public sealed record FlipView
{
    /// <summary>Scan direction, degrees (0 = left to right). The picture glints when the light and your eye line up along it.</summary>
    public double AngleDeg { get; init; }
    /// <summary>Grating pitch (pulse spacing), µm.</summary>
    public double PitchUm { get; init; } = 1.0;
    /// <summary>Parts darker than this (CIELAB L, 0–100) are marked and glint; lighter parts stay mirror.</summary>
    public double Threshold { get; init; } = 50;
    /// <summary>Mark the light parts instead of the dark ones.</summary>
    public bool Invert { get; init; }
    /// <summary>Show greys as a share of marked patches (ordered dither) rather than all-or-nothing.</summary>
    public bool Tones { get; init; }
    /// <summary>Share of this picture's marked patches kept, % (lower = fainter: for a hidden mark).</summary>
    public double StrengthPct { get; init; } = 100;
    /// <summary>Power for this picture's layer, % (null = the plate's power).</summary>
    public double? Power { get; init; }
}

/// <summary>Settings for a flip picture: two to four pictures in one plate, each seen from its own tilt.</summary>
public sealed record FlipOptions
{
    public const int MaxViews = 4;

    public ImmutableList<FlipView> Views { get; init; } = [new() { AngleDeg = 0 }, new() { AngleDeg = 90 }];
    /// <summary>Size of one patch (one picture's grating), mm. Each picture gets one patch in every 2–4.</summary>
    public double PatchMm { get; init; } = 0.3;
    public FlipWeave Weave { get; init; } = FlipWeave.Tiles;

    // The marking (as Diffraction: what has worked on polished stainless).
    public double Power { get; init; } = 32;
    public double Speed { get; init; } = 300;
    public int PulseWidthNs { get; init; } = 60;
    public double LineSpacingMm { get; init; } = 0.06;
    public double OverscanMm { get; init; } = 0.2;

    /// <summary>
    /// Directions spread evenly over 0–180° for <paramref name="n"/> pictures (2: 0° and 90°,
    /// 3: 0°, 60°, 120°, 4: every 45°): the further apart, the less one picture shows at another's tilt.
    /// </summary>
    public static IReadOnlyList<double> SpreadAngles(int n)
    {
        n = Math.Clamp(n, 1, MaxViews);
        return Enumerable.Range(0, n).Select(i => 180.0 * i / n).ToList();
    }
}

/// <summary>
/// Flip pictures on metal: two to four pictures woven into one polished-stainless plate as
/// diffraction gratings, each picture with its own grating direction (or pitch), so each lights up
/// only when the plate is tilted its way. Banknote security holograms work the same way.
/// <para>The plate is split into patches; patch (c, r) belongs to picture <see cref="Owner"/>, and is
/// marked with that picture's grating where that picture is dark (or light, inverted). The other
/// pictures leave it bare, so each picture covers 1/n of the plate at most: the price of the trick is
/// that each picture is dimmer than a plain diffraction picture.</para>
/// </summary>
public static class FlipPicture
{
    public const string LayerPrefix = "Flip ";

    /// <summary>The picture names used in layer names and the window: A, B, C, D.</summary>
    public static string Letter(int i) => ((char)('A' + i)).ToString();

    /// <summary>Which picture patch (c, r) belongs to, for <paramref name="n"/> pictures.</summary>
    public static int Owner(int c, int r, int n, FlipWeave weave)
    {
        if (n <= 1) return 0;
        return weave switch
        {
            FlipWeave.Columns => c % n,
            FlipWeave.Rows => r % n,
            FlipWeave.Diagonal => (c + r) % n,
            // Tiles: 2 → checkerboard; 4 → 2 × 2 tile; 3 → a staggered tile so no picture lines up in a column or row.
            _ => n switch
            {
                2 => (c + r) % 2,
                4 => (c % 2) + 2 * (r % 2),
                _ => (c + 2 * r) % n,
            },
        };
    }

    // 4 × 4 Bayer matrix (0–15): an even spread of thresholds for tones.
    static readonly int[] Bayer = [0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5];

    /// <summary>
    /// An ordered threshold (0–1) for a picture's own patches: the patches a picture owns form a
    /// regular sub-grid, so the dither runs over that grid (not the plate's), keeping its pattern even.
    /// </summary>
    static double OrderedThreshold(int c, int r, int n, FlipWeave weave, int salt)
    {
        // Each picture's patches, numbered along its own lattice.
        int u, v;
        switch (weave)
        {
            case FlipWeave.Columns: u = c / n; v = r; break;
            case FlipWeave.Rows: u = c; v = r / n; break;
            case FlipWeave.Tiles when n == 4: u = c / 2; v = r / 2; break;
            default: u = c / n; v = r; break; // checkerboard, diagonal and 3-tile: about one in n along a row
        }
        u += salt;
        v += salt * 3;
        return (Bayer[(v & 3) * 4 + (u & 3)] + 0.5) / 16;
    }

    /// <summary>A second, unrelated threshold for <see cref="FlipView.StrengthPct"/> (so a faint, toned picture doesn't lose the same patches twice).</summary>
    static double Scatter(int c, int r, int salt)
    {
        uint h = (uint)(c * 73856093 ^ r * 19349663 ^ (salt + 1) * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return (h & 0xFFFF) / 65536.0;
    }

    /// <summary>
    /// Work out the patches for a plate of <paramref name="sizeMm"/>. Picture i is
    /// <paramref name="pictures"/>[i], stretched over the whole plate. Bucket i is picture i (all
    /// buckets are there even if one has nothing marked).
    /// </summary>
    public static GratingMap Map(IReadOnlyList<RasterImage> pictures, Vec2 sizeMm, FlipOptions o, MachineProfile? machine = null)
    {
        var m = machine ?? MachineProfile.F2Ultra;
        int n = Math.Min(Math.Min(pictures.Count, o.Views.Count), FlipOptions.MaxViews);
        if (n < 1) throw new ArgumentException("A flip picture needs at least one picture.", nameof(pictures));
        var lim = m.MopaLimitFor(o.PulseWidthNs) ?? m.Mopa.MinBy(x => Math.Abs(x.PulseWidthNs - o.PulseWidthNs))!;
        double px = Math.Max(0.05, o.PatchMm);
        int cols = Math.Max(1, (int)Math.Round(sizeMm.X / px)), rows = Math.Max(1, (int)Math.Round(sizeMm.Y / px));
        var cells = new int[cols * rows];
        var counts = new int[n];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                int k = Owner(c, r, n, o.Weave);
                var v = o.Views[k];
                // Sample the picture over its patch, widened to the whole tile so thin columns don't miss detail.
                double fx0 = (double)c / cols, fy0 = (double)r / rows, fx1 = (double)(c + 1) / cols, fy1 = (double)(r + 1) / rows;
                var lab = Diffraction.Average(pictures[k], fx0, fy0, fx1, fy1, out bool clear);
                int cell = -1;
                if (!clear)
                {
                    double ink = v.Invert ? lab.L / 100 : 1 - lab.L / 100; // 1 = fully marked
                    bool on = v.Tones
                        ? ink > OrderedThreshold(c, r, n, o.Weave, k)
                        : (v.Invert ? lab.L > v.Threshold : lab.L < v.Threshold);
                    if (on && v.StrengthPct < 100) on = Scatter(c, r, k) * 100 < v.StrengthPct;
                    if (on) cell = k;
                }
                cells[r * cols + c] = cell;
                if (cell >= 0) counts[k]++;
            }
        var buckets = Enumerable.Range(0, n).Select(i =>
        {
            var v = o.Views[i];
            int khz = lim.Safe((int)Math.Round(o.Speed / Math.Max(0.05, v.PitchUm)));
            return new GratingBucket(i, Norm(v.AngleDeg), Diffraction.PitchUm(o.Speed, khz), khz, counts[i], Swatch(i));
        }).ToList();
        return new(cols, rows, cells, buckets, px);
    }

    /// <summary>An angle as 0–180° (a grating's direction has no front and back).</summary>
    public static double Norm(double deg) => (deg % 180 + 180) % 180;

    /// <summary>A preview colour per picture: A blue, B orange, C green, D purple.</summary>
    public static uint Swatch(int i) => i switch { 0 => 0x3B82F6u, 1 => 0xF97316u, 2 => 0x22C55Eu, _ => 0xA855F7u };

    /// <summary>
    /// Which way to tilt the plate to see a grating scanned at <paramref name="angleDeg"/> (as it
    /// lies on screen): its light spreads along the scan direction, so you see it when the lamp and
    /// your eye are on a line along it.
    /// </summary>
    public static string TiltHint(double angleDeg)
    {
        double a = Norm(angleDeg);
        if (a < 22.5 || a >= 157.5) return "tilt left or right";
        if (a < 67.5) return "tilt along the ↘ diagonal";
        if (a < 112.5) return "tilt towards or away from you";
        return "tilt along the ↙ diagonal";
    }

    /// <summary>
    /// Add the flip picture's grating layers over the first picture (the plate takes its place,
    /// size and angle; the others are stretched to fit it). One fill layer per picture
    /// ("Flip A · 0° · 1.00 µm"), patches merged into strips. Returns the new state and the shapes added.
    /// </summary>
    public static (DocState State, IReadOnlyList<Guid> Added) Add(DocState s, IReadOnlyList<ImageShape> pictures, FlipOptions o, MachineProfile? machine = null)
    {
        if (pictures.Count < 1) throw new ArgumentException("Select the pictures first.", nameof(pictures));
        var m = machine ?? MachineProfile.F2Ultra;
        var plate = pictures[0];
        var map = Map(pictures.Select(p => p.Source.Pixels).ToList(), plate.SizeMm, o, m);
        var lim = m.MopaLimitFor(o.PulseWidthNs) ?? m.Mopa.MinBy(x => Math.Abs(x.PulseWidthNs - o.PulseWidthNs))!;
        double cw = plate.SizeMm.X / map.Cols, ch = plate.SizeMm.Y / map.Rows;
        var added = new List<Guid>();
        int layerIndex = s.Layers.Count;
        foreach (var b in map.Buckets.Where(b => b.Pixels > 0))
        {
            var v = o.Views[b.Index];
            var subpaths = PixelGrid.Shape(map.Cols, map.Rows, map.Cells, b.Index, cw, ch, plate.Transform);
            var layer = Layer.Default(layerIndex, LayerMode.Fill) with
            {
                Name = $"{LayerPrefix}{Letter(b.Index)} · {b.AngleDeg:0.#}° · {b.PitchUm:0.00} µm",
                Color = b.Preview,
                Laser = new LaserSettings
                {
                    Light = LightSource.Red,
                    Power = Math.Clamp(v.Power ?? o.Power, 0, 100),
                    Speed = Math.Clamp(o.Speed, m.MinSpeed, m.MaxSpeed),
                    PulseWidthNs = lim.PulseWidthNs,
                    FrequencyKhz = b.FrequencyKhz,
                },
                LinesPerCm = Math.Clamp(10 / Math.Max(1e-3, o.LineSpacingMm), 1, FillBlock.MaxLinesPerCm),
                FillAngleDeg = b.AngleDeg + plate.Transform.RotationDeg,
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

    /// <summary>
    /// What the plate looks like tilted towards picture <paramref name="view"/> (or all at once
    /// for −1), for the preview: bright where that picture's patches glint, a faint ghost of the
    /// others (a real grating leaks a little at other angles), dark mirror elsewhere. One byte per patch.
    /// </summary>
    public static byte[] Simulate(GratingMap map, int view)
    {
        var o = new byte[map.Cells.Length];
        for (int i = 0; i < o.Length; i++)
        {
            int k = map.Cells[i];
            o[i] = k < 0 ? (byte)40 : view < 0 || k == view ? (byte)235 : (byte)70;
        }
        return o;
    }
}

/// <summary>Settings for a flip picture suggested from a grating test photographed from the five views.</summary>
/// <param name="View">Which picture.</param>
/// <param name="Target">The view it should show from.</param>
/// <param name="PitchUm">The pitch of the square that showed best there (and least at the others' views).</param>
/// <param name="Power">That square's power.</param>
/// <param name="AngleDeg">The scan direction to use.</param>
/// <param name="Separation">How much more vivid it was at its view than the others' best (CIELAB chroma); under ~10 the pictures will bleed into each other.</param>
/// <param name="Speed">The test's speed, mm/s (the pitch holds only at this speed).</param>
/// <param name="PulseWidthNs">The test's pulse width.</param>
public sealed record FlipSuggestion(int View, GratingView Target, double PitchUm, double Power, double AngleDeg, double Separation, double Speed, int PulseWidthNs);

public static class FlipSuggest
{
    /// <summary>
    /// For each picture's target view, the grating test square that lit up most there and least at
    /// the other pictures' views. The test squares were scanned left to right (0°), so they speak
    /// for Left, Right and Straight; a Top or Bottom target uses the same square turned 90°, read
    /// from Left or Right (which is the same tilt turned), so treat those as a good guess.
    /// Null when the set has no usable squares.
    /// </summary>
    public static IReadOnlyList<FlipSuggestion>? FromGratings(GratingSet set, IReadOnlyList<GratingView> targets)
    {
        // Chroma of every square from every view.
        var chroma = new Dictionary<(int, int), (ProcessSettings S, Dictionary<GratingView, double> By)>();
        foreach (var v in set.Views)
            foreach (var s in v.Swatches.Where(s => s.Use))
            {
                if (!chroma.TryGetValue((s.Row, s.Col), out var e)) chroma[(s.Row, s.Col)] = e = (s.Settings, []);
                e.By[v.View] = s.Colour.Chroma;
            }
        if (chroma.Count == 0) return null;
        // A Top/Bottom target is read as Left/Right with the grating turned 90°.
        static (GratingView Read, double Turn) Read(GratingView t) => t switch
        {
            GratingView.Top => (GratingView.Left, 90),
            GratingView.Bottom => (GratingView.Right, 90),
            _ => (t, 0),
        };
        var list = new List<FlipSuggestion>();
        for (int i = 0; i < targets.Count; i++)
        {
            var (read, turn) = Read(targets[i]);
            // The other pictures' views, as seen by a grating at this one's turn.
            var others = targets.Where((_, j) => j != i).Select(t => Read(t)).Where(x => x.Turn == turn).Select(x => x.Read).Distinct().ToList();
            var best = chroma.Values
                .Select(e =>
                {
                    double here = e.By.GetValueOrDefault(read);
                    double there = others.Count == 0 ? 0 : others.Max(o => e.By.GetValueOrDefault(o));
                    return (e.S, Here: here, Sep: here - there);
                })
                .OrderByDescending(x => x.Sep + 0.25 * x.Here)
                .First();
            double pitch = Diffraction.PitchUm(best.S.Laser.Speed, best.S.Laser.FrequencyKhz);
            list.Add(new FlipSuggestion(i, targets[i], pitch, best.S.Laser.Power, FlipPicture.Norm(best.S.FillAngleDeg + turn), best.Sep, best.S.Laser.Speed, best.S.Laser.PulseWidthNs));
        }
        return list;
    }
}
