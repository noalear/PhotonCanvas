using System.Globalization;

namespace PhotonCanvas.Core;

/// <summary>
/// Anything that makes a job invalid. Messages are written for the person
/// running the machine, not for a developer.
/// </summary>
public class JobException(string message) : Exception(message)
{
    /// <summary>The shapes the problem is about (the job bar's Checks offers to select them); empty when it isn't about shapes.</summary>
    public IReadOnlyList<Guid> ShapeIds { get; init; } = [];

    /// <summary>The checks found before the job stopped being made (an add-on's advice can say what's wrong).</summary>
    public IReadOnlyList<string> Warnings { get; set; } = [];

    /// <summary>Throws a readable error when <paramref name="value"/> is outside min–max.</summary>
    public static void CheckRange(string what, double value, double min, double max, string unit)
    {
        if (!double.IsFinite(value))
            throw new JobException($"{what} is not a valid number");
        if (value < min || value > max)
            throw new JobException(
                $"{Cap(what)} must be between {Num(min)} and {Num(max)}{Unit(unit)} (got {Num(value)}{Unit(unit)})");
    }

    internal static string Num(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    static string Unit(string unit) => unit.Length == 0 ? "" : unit == "%" ? "%" : " " + unit;

    static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}

/// <summary>The job has nothing the laser would actually process.</summary>
public sealed class EmptyJobException() : JobException("The job has nothing to process.");

/// <summary>A coordinate the laser would visit lies outside the galvo field.</summary>
public sealed class OutsideWorkAreaException : JobException
{
    /// <param name="y0">Where the area starts in Y (the rotary's runs half way round each way from the row that faces the laser).</param>
    /// <param name="item">Which of the job's items (0 = first) the point is in, when known.</param>
    public OutsideWorkAreaException(double x, double y, double w, double h, string context, double y0 = 0, int? item = null)
        : base(FormattableString.Invariant($"Point ({x:0.###}, {y:0.###}) mm is outside the work area (0–{w:0.###} × {y0:0.###}–{y0 + h:0.###} mm){context}."))
    {
        X = x; Y = y; Item = item;
    }

    OutsideWorkAreaException(string message, double x, double y, int? item) : base(message)
    {
        X = x; Y = y; Item = item;
    }

    public double X { get; }
    public double Y { get; }
    public int? Item { get; }

    /// <summary>The same problem said another way (naming the shapes that go outside), with the shapes to select.</summary>
    public OutsideWorkAreaException Explained(string message, IReadOnlyList<Guid> shapes) => new(message, X, Y, Item) { ShapeIds = shapes };
}
