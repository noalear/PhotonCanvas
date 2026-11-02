using System.Text.Json;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Addon.ObjectJig;

/// <summary>A jig kept for next time: the object's outline and the jig's settings (not the design, and not the laser settings).</summary>
public sealed record SavedJig
{
    public string Name { get; init; } = "";
    /// <summary>The object's outline (SVG path data, bed mm, where it lay).</summary>
    public string Object { get; init; } = "";
    public double HeightMm { get; init; }
    public JigBuild Build { get; init; }
    public double SheetMm { get; init; }
    public double DepthMm { get; init; }
    public double GapMm { get; init; }
    public double RoundInsideMm { get; init; }
    public JigNotches Notches { get; init; }
    public double NotchDiameterMm { get; init; }
    public bool LayStraight { get; init; }
    public int Columns { get; init; } = 1;
    public int Rows { get; init; } = 1;
    public double WallMm { get; init; }
    public double MarginMm { get; init; }
    public double CornerRadiusMm { get; init; }
    public double MarkerMm { get; init; }
    public double PinDiameterMm { get; init; }
    public bool EngraveName { get; init; }
    public double LabelSizeMm { get; init; }
    public double? CentreX { get; init; }
    public double? CentreY { get; init; }

    public static SavedJig From(string name, PathGeometry obj, JigOptions o) => new()
    {
        Name = name.Trim(), Object = obj.ToSvgPathData(), HeightMm = o.ObjectHeightMm, Build = o.Build, SheetMm = o.SheetMm, DepthMm = o.DepthMm,
        GapMm = o.GapMm, RoundInsideMm = o.RoundInsideMm, Notches = o.Notches, NotchDiameterMm = o.NotchDiameterMm, LayStraight = o.LayStraight,
        Columns = o.Columns, Rows = o.Rows, WallMm = o.WallMm, MarginMm = o.MarginMm, CornerRadiusMm = o.CornerRadiusMm, MarkerMm = o.MarkerMm,
        PinDiameterMm = o.PinDiameterMm, EngraveName = o.EngraveName, LabelSizeMm = o.LabelSizeMm, CentreX = o.Centre?.X, CentreY = o.Centre?.Y,
    };

    public PathGeometry ObjectOutline() => PathGeometry.FromSvgPathData(Object);

    /// <summary>The jig's settings, on top of <paramref name="o"/> (which keeps its laser settings and design placement).</summary>
    public JigOptions ApplyTo(JigOptions o) => o with
    {
        Name = Name, ObjectHeightMm = HeightMm, Build = Build, SheetMm = SheetMm, DepthMm = DepthMm, GapMm = GapMm, RoundInsideMm = RoundInsideMm,
        Notches = Notches, NotchDiameterMm = NotchDiameterMm, LayStraight = LayStraight, Columns = Columns, Rows = Rows, WallMm = WallMm,
        MarginMm = MarginMm, CornerRadiusMm = CornerRadiusMm, MarkerMm = MarkerMm, PinDiameterMm = PinDiameterMm, EngraveName = EngraveName,
        LabelSizeMm = LabelSizeMm, Centre = CentreX is { } x && CentreY is { } y ? new Vec2(x, y) : null,
    };
}

/// <summary>The saved jigs: one JSON file each in the add-on's data folder.</summary>
public sealed class JigStore(string folder)
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    string Dir => Path.Combine(folder, "jigs");

    public List<SavedJig> All()
    {
        var o = new List<SavedJig>();
        if (!Directory.Exists(Dir)) return o;
        foreach (var f in Directory.GetFiles(Dir, "*.json"))
        {
            try { if (JsonSerializer.Deserialize<SavedJig>(File.ReadAllText(f), Json) is { Object.Length: > 0 } j) o.Add(j); }
            catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or FormatException) { }
        }
        return [.. o.OrderBy(j => j.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    public void Save(SavedJig jig)
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FileFor(jig.Name), JsonSerializer.Serialize(jig, Json));
    }

    public void Delete(string name)
    {
        var f = FileFor(name);
        if (File.Exists(f)) File.Delete(f);
    }

    string FileFor(string name)
    {
        var bad = Path.GetInvalidFileNameChars();
        var safe = new string([.. name.Trim().Select(ch => bad.Contains(ch) ? '_' : ch)]);
        return Path.Combine(Dir, (safe.Length == 0 ? "Jig" : safe) + ".json");
    }
}
