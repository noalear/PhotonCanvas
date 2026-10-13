using System.Globalization;

namespace YTool.Core;

/// <summary>
/// Anything that makes a job invalid. Messages are written for the person
/// running the machine, not for a developer.
/// </summary>
public class JobException(string message) : Exception(message)
{
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
public sealed class OutsideWorkAreaException(double x, double y, double w, double h, string context)
    : JobException($"Point ({x:0.###}, {y:0.###}) mm is outside the work area (0–{w:0.###} × 0–{h:0.###} mm){context}.")
{
    public double X { get; } = x;
    public double Y { get; } = y;
}
