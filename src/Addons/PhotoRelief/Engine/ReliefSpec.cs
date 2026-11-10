using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace PhotonCanvas.Addon.PhotoRelief;

/// <summary>What in the photo is the subject (raised and rounded); the rest is background.</summary>
public enum SubjectFrom
{
    /// <summary>All of it: the whole photo is carved as one relief.</summary>
    WholePicture,
    /// <summary>Everything except a plain background found round the photo's edges (a wall, a sheet, the sky).</summary>
    PlainBackground,
    /// <summary>The photo's own see-through parts are the background (a photo with its background already removed).</summary>
    SeeThrough,
}

/// <summary>What happens to the background round the subject.</summary>
public enum BackgroundStyle
{
    /// <summary>Carved down to a flat field, so the subject stands up out of it.</summary>
    CarvedDown,
    /// <summary>Left as the surface: the subject is carved down into it (a sunk relief).</summary>
    NotCarved,
}

/// <summary>The plaque the relief is carved on.</summary>
public enum PlaqueShape
{
    /// <summary>No plaque: just the picture, edge to edge.</summary>
    None,
    Rectangle,
    Rounded,
    Oval,
    Circle,
}

/// <summary>Everything that says how a photo becomes a relief (the photo itself is kept beside it).</summary>
public sealed record ReliefSpec
{
    /// <summary>The style last picked (Portrait, Coin or medal…), only to show it again.</summary>
    public string Style { get; init; } = "Portrait";
    /// <summary>Darker parts of the photo stand higher (else lighter ones do).</summary>
    public bool DarkIsHigh { get; init; }

    public SubjectFrom Subject { get; init; } = SubjectFrom.PlainBackground;
    /// <summary>0–100: how different a colour may be from the edges' and still count as background.</summary>
    public double Tolerance { get; init; } = 22;
    /// <summary>Places clicked on the background (fractions of the photo across and down): their colours are background too.</summary>
    public IReadOnlyList<PhotoPoint> Picks { get; init; } = [];
    public BackgroundStyle Background { get; init; } = BackgroundStyle.CarvedDown;
    /// <summary>How far down the background field is, as a % of the whole depth (100 = the deepest).</summary>
    public double BackgroundDepth { get; init; } = 70;

    public PlaqueShape Shape { get; init; } = PlaqueShape.Oval;
    /// <summary>Across (mm); a circle's diameter.</summary>
    public double WidthMm { get; init; } = 100;
    /// <summary>Down (mm), when <see cref="KeepPhotoShape"/> is off.</summary>
    public double HeightMm { get; init; } = 130;
    /// <summary>The height follows the photo's own shape (not for a circle).</summary>
    public bool KeepPhotoShape { get; init; } = true;
    /// <summary>A rounded plaque's corner radius (mm).</summary>
    public double CornerMm { get; init; } = 10;
    /// <summary>A raised rim round the plaque's edge this wide (mm); 0 = none.</summary>
    public double RimMm { get; init; } = 4;

    /// <summary>1 = the photo just fills the plaque; more crops in closer.</summary>
    public double Zoom { get; init; } = 1;
    /// <summary>-1 … 1: which part of the photo shows when it's cropped (0 = the middle).</summary>
    public double PanX { get; init; }
    public double PanY { get; init; }

    /// <summary>0–100: fine detail (hair, fur, texture); 50 = as in the photo.</summary>
    public double Detail { get; init; } = 55;
    /// <summary>0–100: broad shading (the big light and dark areas); 50 = as in the photo.</summary>
    public double Form { get; init; } = 35;
    /// <summary>0–100: big jumps between light and dark are flattened so they don't carve as cliffs.</summary>
    public double EvenOut { get; init; } = 70;
    /// <summary>0–100: smooths skin, grain and noise but keeps edges.</summary>
    public double Smooth { get; init; } = 40;
    /// <summary>0–100: how much of the depth goes to raising the subject as a rounded shape (the rest is its detail).</summary>
    public double Raise { get; init; } = 50;
    /// <summary>How far in from the subject's edge (mm) its rounding reaches full height; 0 = a flat-topped step.</summary>
    public double RoundingMm { get; init; } = 22;

    /// <summary>How deep the deepest part is meant to be carved (mm): for the preview and the summary.</summary>
    public double DepthMm { get; init; } = 3;
    /// <summary>Depth map pixels per mm.</summary>
    public double PixelsPerMm { get; init; } = 10;
    /// <summary>A cut line round the plaque, on the Cut layer.</summary>
    public bool CutLine { get; init; }

    /// <summary>Is there a plaque (an outline, outside which nothing is carved)?</summary>
    [JsonIgnore]
    public bool HasPlaque => Shape != PlaqueShape.None;

    /// <summary>The plaque's size (mm) for a photo <paramref name="photoW"/> × <paramref name="photoH"/> pixels.</summary>
    public (double W, double H) Size(int photoW, int photoH)
    {
        double w = Math.Max(1, WidthMm);
        if (Shape == PlaqueShape.Circle) return (w, w);
        if (KeepPhotoShape && photoW > 0 && photoH > 0) return (w, w * photoH / photoW);
        return (w, Math.Max(1, HeightMm));
    }

    /// <summary>Why it can't be made as it is, or null.</summary>
    public string? Problem(int photoW, int photoH)
    {
        var (w, h) = Size(photoW, photoH);
        if (w > 3000 || h > 3000) return "That's too big: keep it under 3000 mm a side.";
        if (HasPlaque && RimMm * 2 >= Math.Min(w, h) - 2) return "The rim is as wide as the plaque: make it narrower.";
        return null;
    }

    // ---- saving ----

    // Its own resolver, so the program's shared JSON cache never holds this add-on's types (docs/addons.md).
    static readonly JsonSerializerOptions Json = new()
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>A spec read back, or null when the text isn't one.</summary>
    public static ReliefSpec? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<ReliefSpec>(json, Json); }
        catch (Exception e) when (e is JsonException or NotSupportedException or ArgumentException) { return null; }
    }
}

/// <summary>A point on the photo, as fractions of its width and height (0–1).</summary>
public sealed record PhotoPoint(double U, double V);

/// <summary>A ready-made starting point: what it's for, and the settings it puts in.</summary>
public sealed record ReliefStyle(string Name, string About, Func<ReliefSpec, ReliefSpec> Apply);

/// <summary>The ready-made styles. Each keeps the size and photo crop, and sets the rest.</summary>
public static class ReliefStyles
{
    public static readonly IReadOnlyList<ReliefStyle> All =
    [
        new("Portrait", "A face or head and shoulders raised and rounded off a plain background, on an oval plaque with a rim.",
            s => s with
            {
                DarkIsHigh = false, Subject = SubjectFrom.PlainBackground, Background = BackgroundStyle.CarvedDown, BackgroundDepth = 70,
                Shape = PlaqueShape.Oval, RimMm = 4, Detail = 55, Form = 35, EvenOut = 70, Smooth = 40, Raise = 50, RoundingMm = 22,
            }),
        new("Pet", "A pet's head raised off a plain background, with more fur detail kept, on a rounded plaque.",
            s => s with
            {
                DarkIsHigh = false, Subject = SubjectFrom.PlainBackground, Background = BackgroundStyle.CarvedDown, BackgroundDepth = 70,
                Shape = PlaqueShape.Rounded, CornerMm = 10, RimMm = 4, Detail = 75, Form = 35, EvenOut = 70, Smooth = 15, Raise = 45, RoundingMm = 18,
            }),
        new("Coin or medal", "A low, rounded profile on a round medal with a rim, like a coin: little detail, strong shape.",
            s => s with
            {
                DarkIsHigh = false, Subject = SubjectFrom.PlainBackground, Background = BackgroundStyle.CarvedDown, BackgroundDepth = 60,
                Shape = PlaqueShape.Circle, RimMm = 4, Detail = 35, Form = 25, EvenOut = 85, Smooth = 55, Raise = 70, RoundingMm = 14,
            }),
        new("Landscape or building", "The whole photo as a relief: sky, hills and buildings from their shading, on a plain rectangle.",
            s => s with
            {
                DarkIsHigh = false, Subject = SubjectFrom.WholePicture, Shape = PlaqueShape.Rectangle, RimMm = 3,
                Detail = 55, Form = 60, EvenOut = 55, Smooth = 25, Raise = 0, RoundingMm = 0,
            }),
        new("Logo or clip art", "Flat-coloured artwork on a plain background made puffy: each shape rounded up, the background carved away.",
            s => s with
            {
                DarkIsHigh = false, Subject = SubjectFrom.PlainBackground, Background = BackgroundStyle.CarvedDown, BackgroundDepth = 100,
                Shape = PlaqueShape.None, RimMm = 0, Detail = 20, Form = 0, EvenOut = 90, Smooth = 30, Raise = 85, RoundingMm = 5,
            }),
        new("Sunk relief", "The subject carved down into the surface and rounded inside its outline, the background left untouched, like Egyptian carving.",
            s => s with
            {
                DarkIsHigh = false, Subject = SubjectFrom.PlainBackground, Background = BackgroundStyle.NotCarved,
                Shape = PlaqueShape.None, RimMm = 0, Detail = 55, Form = 35, EvenOut = 70, Smooth = 35, Raise = 55, RoundingMm = 12,
            }),
        new("Straight brightness", "Height straight from brightness, nothing evened out or raised: the plainest kind, as most programs do it.",
            s => s with
            {
                Subject = SubjectFrom.WholePicture, Shape = PlaqueShape.None, RimMm = 0,
                Detail = 50, Form = 50, EvenOut = 0, Smooth = 0, Raise = 0, RoundingMm = 0,
            }),
    ];

    public static ReliefStyle? Find(string name) => All.FirstOrDefault(s => s.Name == name);
}
