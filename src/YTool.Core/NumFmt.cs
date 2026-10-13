using System.Globalization;

namespace YTool.Core;

/// <summary>Number formatting that matches what xTool's generator writes.</summary>
public static class NumFmt
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Vector coordinates: up to 3 decimals, trailing zeros trimmed (<c>100</c>, <c>154.101</c>).</summary>
    public static string Trim3(double v)
    {
        var s = v.ToString("F3", Inv).TrimEnd('0').TrimEnd('.');
        return s is "-0" or "" ? "0" : s;
    }

    /// <summary>Fill/bitmap coordinates: always 3 decimals (<c>100.000</c>).</summary>
    public static string Fixed3(double v)
    {
        var s = v.ToString("F3", Inv);
        return s == "-0.000" ? "0.000" : s;
    }

    /// <summary>
    /// A number as JavaScript's <c>JSON.stringify</c> prints it: whole values
    /// without <c>.0</c>, others in shortest round-trip form.
    /// </summary>
    public static string JsNumber(double v)
    {
        if (!double.IsFinite(v)) return "null";
        if (v == Math.Floor(v) && Math.Abs(v) < 1e15) return ((long)v).ToString(Inv);
        var s = v.ToString("R", Inv);
        // .NET writes 1E-07; JavaScript writes 1e-7.
        int e = s.IndexOf('E');
        if (e >= 0)
        {
            var exp = int.Parse(s[(e + 1)..], Inv);
            s = s[..e] + "e" + (exp >= 0 ? "+" : "-") + Math.Abs(exp).ToString(Inv);
        }
        return s;
    }

    /// <summary>Round to <paramref name="digits"/> decimals, then print JS-style.</summary>
    public static string JsRounded(double v, int digits)
    {
        double p = Math.Pow(10, digits);
        return JsNumber(Math.Round(v * p, MidpointRounding.AwayFromZero) / p);
    }

    /// <summary>Plain invariant integer.</summary>
    public static string Int(long v) => v.ToString(Inv);
}
