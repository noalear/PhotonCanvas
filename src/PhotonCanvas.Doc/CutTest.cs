using PhotonCanvas.Core;

namespace PhotonCanvas.Doc;

/// <summary>What the second axis of a cut test tries.</summary>
public enum CutTestDown { Passes, Power }

/// <summary>A cut test: small squares cut at speeds across and passes (or power) down; the ones that drop out went through.</summary>
public sealed record CutTestOptions
{
    public LightSource Light { get; init; } = LightSource.Blue;
    /// <summary>Power when passes are tried down the side.</summary>
    public double Power { get; init; } = 100;
    /// <summary>Passes when power is tried down the side.</summary>
    public int Passes { get; init; } = 1;
    public double SpeedMin { get; init; } = 4;
    public double SpeedMax { get; init; } = 24;
    public int SpeedSteps { get; init; } = 6;
    public CutTestDown Down { get; init; } = CutTestDown.Passes;
    public double DownMin { get; init; } = 1;
    public double DownMax { get; init; } = 3;
    public int DownSteps { get; init; } = 3;
    public double CellMm { get; init; } = 8;
    public double GapMm { get; init; } = 4;
    public double LabelMm { get; init; } = 2.5;
    public string Font { get; init; } = "Arial";
    /// <summary>Top-left corner of the test on the bed.</summary>
    public Vec2 Origin { get; init; } = new(10, 10);
}

/// <summary>A kerf and fit test at one cut setting: a square to measure, and a comb of slots to try it in.</summary>
public sealed record KerfTestOptions
{
    public LaserSettings Laser { get; init; } = LaserSettings.Blue(100, 10);
    public double ThicknessMm { get; init; } = 3;
    /// <summary>The square's drawn size: measured, it gives the kerf.</summary>
    public double SquareMm { get; init; } = 20;
    /// <summary>Each slot is the thickness plus one of these.</summary>
    public IReadOnlyList<double> Fits { get; init; } = [-0.2, -0.1, 0, 0.1, 0.2, 0.3];
    public double LabelMm { get; init; } = 2.2;
    public string Font { get; init; } = "Arial";
    public Vec2 Origin { get; init; } = new(10, 10);
}

/// <summary>
/// Cut tests. The grid: every square on its own layer with its own speed and passes (or power),
/// cut with no kerf allowance; the squares that drop out (or push out with a finger) went through.
/// The quickest of those is the one to use, usually with the least char. Then, at that setting, a
/// square to measure (its size short of what was drawn is the kerf) and a comb of slots, each the
/// material's thickness plus a little more or less: the one the square slides into snugly is the
/// fit for slotted builds (egg crate, radial, boxes).
/// </summary>
public static class CutTest
{
    public const string LayerPrefix = "Cut test · ";
    public const string KerfLayerName = "Kerf test";

    /// <summary>Is this one of a cut test's squares?</summary>
    public static bool IsCell(Layer l) => l.Name.StartsWith(LayerPrefix, StringComparison.Ordinal);

    /// <summary>The kerf and fit test's cutting layer, if the design has one.</summary>
    public static Layer? KerfLayer(DocState s) => s.Layers.FirstOrDefault(l => l.Name == KerfLayerName);

    /// <summary>"12 mm/s · 2 passes" (or "· 80%").</summary>
    public static string Describe(LaserSettings l) =>
        $"{l.Power:0.#}% · {l.Speed:0.#} mm/s · {l.Passes} pass{(l.Passes == 1 ? "" : "es")}";

    /// <summary>How long a setting takes per mm of cut: passes ÷ speed. Lower is quicker.</summary>
    public static double SecondsPerMm(LaserSettings l) => l.Passes / Math.Max(0.01, l.Speed);

    static Layer Labels(int index, LightSource light, double cutPower) => Layer.Default(index, LayerMode.Score) with
    {
        Name = "Cut test labels",
        // A light mark, nowhere near cutting.
        Laser = light == LightSource.Red ? new LaserSettings { Light = LightSource.Red, Power = 20, Speed = 1000 } : LaserSettings.Blue(Math.Clamp(cutPower * 0.12, 3, 15), 200),
    };

    static TextShape Text(string text, TextStyle style, Guid layer, double cx, double cy, bool right = false)
    {
        var t = TextShape.Create(text, style, layer, Affine.Identity);
        var b = t.Bounds;
        if (b.IsEmpty) return t;
        return (TextShape)t.With(transform: Affine.Translate(right ? cx - b.MaxX : cx - b.Center.X, cy - b.Center.Y));
    }

    /// <summary>Add the cut-through grid. Returns the new state and the ids of everything added.</summary>
    public static (DocState State, IReadOnlyList<Guid> Added) Add(DocState s, CutTestOptions o, MachineProfile? machine = null)
    {
        var m = machine ?? MachineProfile.F2Ultra;
        var speeds = MaterialTest.Values(TestParam.Speed, o.SpeedMin, o.SpeedMax, o.SpeedSteps, m);
        var down = o.Down == CutTestDown.Passes
            ? MaterialTest.Values(TestParam.Passes, o.DownMin, o.DownMax, o.DownSteps, m)
            : MaterialTest.Values(TestParam.Power, o.DownMin, o.DownMax, o.DownSteps, m);
        double pitch = o.CellMm + o.GapMm;
        double labelCol = o.LabelMm * 4 + o.GapMm;
        double headerRow = o.LabelMm * 2 + o.GapMm * 1.5;
        double x0 = o.Origin.X + labelCol, y0 = o.Origin.Y + headerRow;
        var shapes = new List<Shape>();
        int index = s.Layers.Count, firstCell = s.Layers.Count;
        var labels = Labels(index + speeds.Count * down.Count, o.Light, o.Down == CutTestDown.Power ? down.Max() : o.Power);
        for (int r = 0; r < down.Count; r++)
            for (int c = 0; c < speeds.Count; c++)
            {
                double power = o.Down == CutTestDown.Power ? down[r] : o.Power;
                int passes = o.Down == CutTestDown.Passes ? (int)down[r] : o.Passes;
                var laser = LaserSettings.Blue(Math.Clamp(power, 0, 100), speeds[c]) with { Passes = Math.Max(1, passes) };
                if (o.Light == LightSource.Red) laser = laser with { Light = LightSource.Red };
                var layer = Layer.Default(index++, LayerMode.Cut) with { Name = LayerPrefix + Describe(laser), Laser = laser, KerfMm = 0 };
                s = Ops.AddLayer(s, layer);
                shapes.Add(new PathShape { Name = Describe(laser), LayerId = layer.Id, Geometry = PathGeometry.Rect(x0 + c * pitch, y0 + r * pitch, o.CellMm, o.CellMm) });
            }
        // The labels go first: once a square is cut it may drop.
        s = Ops.AddLayer(s, labels, BeforeCuts(s, firstCell));
        var style = new TextStyle(o.Font, o.LabelMm);
        double gridW = speeds.Count * pitch - o.GapMm;
        shapes.Add(Text("mm/s", style, labels.Id, x0 + gridW / 2, o.Origin.Y + o.LabelMm / 2));
        for (int c = 0; c < speeds.Count; c++)
            shapes.Add(Text($"{speeds[c]:0.#}", style, labels.Id, x0 + c * pitch + o.CellMm / 2, y0 - o.GapMm / 2 - o.LabelMm / 2));
        for (int r = 0; r < down.Count; r++)
            shapes.Add(Text(o.Down == CutTestDown.Passes ? $"{down[r]:0}×" : $"{down[r]:0.#}%", style, labels.Id, x0 - o.GapMm / 2, y0 + r * pitch + o.CellMm / 2, right: true));
        shapes.Add(Text(o.Down == CutTestDown.Passes ? $"{o.Power:0.#}%" : $"{o.Passes}×", style, labels.Id, x0 - o.GapMm / 2, o.Origin.Y + o.LabelMm / 2, right: true));
        return (Ops.AddShapes(s, shapes), shapes.Select(x => x.Id).ToList());
    }

    /// <summary>Where light marks go: before every cut layer already in the design (cut pieces can shift or drop).</summary>
    static int BeforeCuts(DocState s, int otherwise)
    {
        int first = s.Layers.FindIndex(l => l.Mode == LayerMode.Cut && l.Output);
        return first >= 0 ? Math.Min(first, otherwise) : otherwise;
    }

    /// <summary>The comb's slot widths, as drawn: thickness plus each fit.</summary>
    public static IReadOnlyList<double> SlotWidths(KerfTestOptions o) => [.. o.Fits.Select(f => Math.Max(0.2, o.ThicknessMm + f))];

    /// <summary>"+0.1", "0", "−0.2".</summary>
    public static string FitLabel(double f) => Math.Abs(f) < 1e-9 ? "0" : f > 0 ? $"+{f:0.##}" : $"−{-f:0.##}";

    /// <summary>Add the kerf and fit test: the square and the slotted comb, cut at the chosen setting (no kerf allowance), labels lightly marked.</summary>
    public static (DocState State, IReadOnlyList<Guid> Added) AddKerf(DocState s, KerfTestOptions o)
    {
        var cut = Layer.Default(s.Layers.Count, LayerMode.Cut) with { Name = KerfLayerName, Laser = o.Laser, KerfMm = 0 };
        var labels = Labels(s.Layers.Count + 1, o.Laser.Light, o.Laser.Power) with { Name = "Kerf test labels" };
        // The labels first: some are on the pieces, which may drop once cut.
        s = Ops.AddLayer(Ops.AddLayer(s, labels, BeforeCuts(s, s.Layers.Count)), cut);
        var style = new TextStyle(o.Font, o.LabelMm);
        var shapes = new List<Shape>();
        double x = o.Origin.X, y = o.Origin.Y, sq = o.SquareMm;
        shapes.Add(new PathShape { Name = $"Kerf square {sq:0.#} mm", LayerId = cut.Id, Geometry = PathGeometry.Rect(x, y, sq, sq) });
        shapes.Add(Text($"{sq:0.#}", style, labels.Id, x + sq / 2, y + sq / 2));

        // The comb: a strip with slots down from its top edge, each labelled with its fit underneath.
        var widths = SlotWidths(o);
        double tooth = 5, depth = Math.Max(6, sq * 0.4), height = depth + o.LabelMm + 5;
        double cx = x + sq + 6, cy = y;
        var pts = new List<Vec2> { new(cx, cy) };
        double at = cx + tooth;
        var centres = new List<double>();
        foreach (var wd in widths)
        {
            pts.Add(new(at, cy));
            pts.Add(new(at, cy + depth));
            pts.Add(new(at + wd, cy + depth));
            pts.Add(new(at + wd, cy));
            centres.Add(at + wd / 2);
            at += wd + tooth;
        }
        pts.Add(new(at, cy));
        pts.Add(new(at, cy + height));
        pts.Add(new(cx, cy + height));
        shapes.Add(new PathShape { Name = "Fit comb", LayerId = cut.Id, Geometry = PathGeometry.Polyline(pts, true) });
        for (int i = 0; i < centres.Count; i++)
            shapes.Add(Text(FitLabel(o.Fits[i]), style with { SizeMm = Math.Min(o.LabelMm, tooth + widths[i] - 0.5) }, labels.Id, centres[i], cy + depth + 1.2 + o.LabelMm / 2));
        return (Ops.AddShapes(s, shapes), shapes.Select(sh => sh.Id).ToList());
    }

    /// <summary>The kerf from the square as measured: how much smaller than drawn it came out (never below 0).</summary>
    public static double Kerf(double drawnMm, double measuredMm) => Math.Max(0, drawnMm - measuredMm);

    /// <summary>
    /// The slot fit to use in the slicer, from the snuggest slot on the comb (cut with no kerf
    /// allowance, so each slot came out a kerf wider than drawn). With the cut layer's kerf set,
    /// slots come out as drawn, so the fit takes in the kerf; with it at 0 the fit is the comb's.
    /// </summary>
    public static double SlotFit(double combFit, double kerfMm, bool layerKerfSet) => layerKerfSet ? combFit + kerfMm : combFit;
}
