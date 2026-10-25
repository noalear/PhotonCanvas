using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace PhotonCanvas.Core;

/// <summary>
/// The F2 Ultra MOPA's limits per pulse width (<see cref="MopaLimit"/>). Not built into the program:
/// read at run time from the user's own installed xTool Studio, the same way the connection keys and
/// presets are, read only. Without xTool Studio a cautious built-in table is used (<see cref="Fallback"/>):
/// lower top frequencies, full power assumed to need higher frequencies, and the whole range below
/// full power kept off highly reflective work for long pulses.
/// </summary>
public static partial class MopaTable
{
    /// <summary>Set to "builtin" to ignore xTool Studio and use <see cref="Fallback"/> (the tests do).</summary>
    public const string SourceVariable = "PHOTONCANVAS_MOPA_TABLE";

    static IReadOnlyList<int> PulseWidths => LaserSettings.PulseWidths;

    static readonly Lazy<(IReadOnlyList<MopaLimit> Table, string? From)> Loaded = new(Load);

    /// <summary>The table in use: xTool Studio's when it could be read, else <see cref="Fallback"/>.</summary>
    public static IReadOnlyList<MopaLimit> Current => Loaded.Value.Table;

    /// <summary>Where <see cref="Current"/> came from: the file it was read from, or null for the built-in fallback.</summary>
    public static string? ReadFrom => Loaded.Value.From;

    /// <summary>
    /// The cautious table for when xTool Studio isn't there. Worked out from simple rules, not copied:
    /// top frequency at most 2000 kHz and at most a 10% duty cycle (100 000 ÷ pulse ns, rounded down to
    /// 10 kHz); full power assumed only from 6000 ÷ pulse ns kHz (at least 120 kHz, rounded up to 10,
    /// never above the top); and from 100 ns up, every frequency below full power counts as protected
    /// on highly reflective work.
    /// </summary>
    public static readonly IReadOnlyList<MopaLimit> Fallback = [.. PulseWidths.Select(FallbackFor)];

    static MopaLimit FallbackFor(int ns)
    {
        int max = Math.Min(2000, 100_000 / ns / 10 * 10);
        int full = Math.Min(max, Math.Max(120, (int)Math.Ceiling(6000.0 / ns / 10) * 10));
        return new MopaLimit(ns, full, max, ns >= 100 ? 1 : null);
    }

    static (IReadOnlyList<MopaLimit>, string?) Load()
    {
        if (string.Equals(Environment.GetEnvironmentVariable(SourceVariable), "builtin", StringComparison.OrdinalIgnoreCase))
            return (Fallback, null);
        foreach (var (file, read) in Candidates())
        {
            try
            {
                if (Parse(read()) is { } table) return (table, file);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                // Unreadable (xTool Studio updating, say): try the next place.
            }
        }
        return (Fallback, null);
    }

    /// <summary>The F2 Ultra's device extension in xTool Studio: the copy it keeps up to date first, then the installed one.</summary>
    static IEnumerable<(string File, Func<string> Read)> Candidates()
    {
        const string Ext = "GS004-CLASS-4";
        var data = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrEmpty(data))
        {
            var js = Path.Combine(data, "xTool Studio", "atomm", "exts", Ext, "index.js");
            if (File.Exists(js)) yield return (js, () => File.ReadAllText(js));
        }
        string[] roots =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        ];
        foreach (var root in roots.Where(r => !string.IsNullOrEmpty(r)))
            foreach (var name in new[] { "xTool Studio", "xToolStudio", "xTool Creative Space" })
            {
                var zip = Path.Combine(root, name, "resources", "exts.zip");
                if (File.Exists(zip)) yield return (zip, () => ReadEntry(zip, Ext + "/index.js"));
            }
    }

    static string ReadEntry(string zip, string entry)
    {
        using var z = ZipFile.OpenRead(zip);
        var e = z.GetEntry(entry) ?? throw new InvalidDataException("No " + entry);
        using var r = new StreamReader(e.Open());
        return r.ReadToEnd();
    }

    // {pulse:[fullPowerKhz,maxKhz(,protectedFromKhz)],…}, numbers possibly written as 4e3.
    [GeneratedRegex(@"\{(?:\d+:\[[0-9.e]+,[0-9.e]+(?:,[0-9.e]+)?\],?){6,}\}")]
    private static partial Regex TablePattern();

    [GeneratedRegex(@"(\d+):\[([0-9.e]+),([0-9.e]+)(?:,([0-9.e]+))?\]")]
    private static partial Regex EntryPattern();

    /// <summary>
    /// The limits table in the extension's code, or null when there isn't exactly one that makes sense
    /// (pulse widths rising, full power at or below the top, protected range below full power).
    /// </summary>
    public static IReadOnlyList<MopaLimit>? Parse(string code)
    {
        List<MopaLimit>? found = null;
        foreach (Match t in TablePattern().Matches(code))
        {
            var table = new List<MopaLimit>();
            foreach (Match e in EntryPattern().Matches(t.Value))
            {
                if (!Num(e.Groups[2].Value, out int full) || !Num(e.Groups[3].Value, out int max) || !int.TryParse(e.Groups[1].Value, out int ns)) { table = null; break; }
                int? gap = null;
                if (e.Groups[4].Success) { if (!Num(e.Groups[4].Value, out int g)) { table = null; break; } gap = g; }
                table.Add(new MopaLimit(ns, full, max, gap));
            }
            if (table is null || !Sensible(table)) continue;
            if (found is not null) return null; // two candidates: don't guess
            found = table;
        }
        return found;
    }

    static bool Sensible(List<MopaLimit> t)
    {
        for (int i = 0; i < t.Count; i++)
        {
            var l = t[i];
            if (l.PulseWidthNs is < 1 or > 2000 || l.DefaultKhz < 1 || l.DefaultKhz > l.MaxKhz || l.MaxKhz > 10_000) return false;
            if (l.GapFromKhz is { } g && (g < 1 || g >= l.DefaultKhz)) return false;
            if (i > 0 && l.PulseWidthNs <= t[i - 1].PulseWidthNs) return false;
        }
        return true;
    }

    static bool Num(string s, out int v)
    {
        v = 0;
        if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) || d != Math.Floor(d) || d is < 0 or > 1e6) return false;
        v = (int)d;
        return true;
    }
}
