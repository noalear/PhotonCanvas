using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace PhotonCanvas.Doc;

/// <summary>
/// What fills in the <c>{fields}</c> of text and codes: a serial number, the date and time,
/// and a row of a spreadsheet (CSV). Saved with the design, so the next number carries on.
/// </summary>
public sealed record VariableData
{
    /// <summary>The serial number the next run marks.</summary>
    public long Serial { get; init; } = 1;
    /// <summary>Added to the serial number after each run (can be negative).</summary>
    public long SerialStep { get; init; } = 1;
    /// <summary>The last serial number to mark; past it, the job won't send. Null = no end.</summary>
    public long? SerialEnd { get; init; }
    /// <summary>After each finished run: next serial number and next spreadsheet row.</summary>
    public bool AdvanceAfterRun { get; init; } = true;
    /// <summary>The spreadsheet's file name (for showing) and its text (kept in the design, so it travels with it).</summary>
    public string CsvName { get; init; } = "";
    public string CsvText { get; init; } = "";
    /// <summary>The spreadsheet row the next run marks, 0 = the first row under the headings.</summary>
    public int CsvRow { get; init; }

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<string, Csv.Table> Parsed = new();

    /// <summary>The spreadsheet, read (cached per text, so records stay plain values).</summary>
    public Csv.Table Table => Parsed.GetValue(CsvText, Csv.Parse);

    public bool HasCsv => Table.Headers.Length > 0;
}

/// <summary>A small CSV reader: quoted fields, doubled quotes, commas, semicolons or tabs.</summary>
public static class Csv
{
    /// <summary>Headings (first row) and the rows under them.</summary>
    public sealed record Table(ImmutableArray<string> Headers, ImmutableArray<ImmutableArray<string>> Rows)
    {
        public static readonly Table Empty = new([], []);

        /// <summary>Column index of a heading (ignoring case and spaces at the ends), or −1.</summary>
        public int Column(string name)
        {
            var n = name.Trim();
            for (int i = 0; i < Headers.Length; i++)
                if (string.Equals(Headers[i].Trim(), n, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        public string Cell(int row, int col) => row >= 0 && row < Rows.Length && col >= 0 && col < Rows[row].Length ? Rows[row][col] : "";
    }

    public static Table Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Table.Empty;
        text = text.TrimStart('﻿');
        // The separator: whichever of , ; tab appears most in the first line.
        var firstLine = text.Split('\n')[0];
        char sep = new[] { ',', ';', '\t' }.OrderByDescending(c => firstLine.Count(x => x == c)).First();
        var rows = new List<ImmutableArray<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        bool quoted = false, any = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else quoted = false;
                }
                else field.Append(c);
                continue;
            }
            if (c == '"' && field.Length == 0) { quoted = true; any = true; }
            else if (c == sep) { row.Add(field.ToString()); field.Clear(); any = true; }
            else if (c == '\r') { }
            else if (c == '\n')
            {
                row.Add(field.ToString());
                field.Clear();
                if (any || row.Any(f => f.Length > 0)) rows.Add([.. row]);
                row.Clear();
                any = false;
            }
            else { field.Append(c); any = true; }
        }
        if (field.Length > 0 || row.Count > 0 || any)
        {
            row.Add(field.ToString());
            if (row.Any(f => f.Length > 0)) rows.Add([.. row]);
        }
        if (rows.Count == 0) return Table.Empty;
        return new Table(rows[0], [.. rows.Skip(1)]);
    }
}

/// <summary>
/// Fields in text and codes: <c>{serial}</c> (or <c>{serial:0000}</c> for leading zeros),
/// <c>{date}</c> / <c>{date:dd/MM/yyyy}</c>, <c>{time}</c> / <c>{time:HH:mm:ss}</c>,
/// <c>{row}</c> (the spreadsheet row number) and <c>{Any heading}</c> from the spreadsheet.
/// <c>{{</c> and <c>}}</c> give plain braces.
/// </summary>
public static class VariableText
{
    /// <summary>The fields a person can insert, with what they do.</summary>
    public static readonly IReadOnlyList<(string Field, string Meaning)> Builtins =
    [
        ("{serial}", "Serial number"),
        ("{serial:0000}", "Serial number, 4 digits (0042)"),
        ("{date}", "Today's date (2026-10-16)"),
        ("{date:dd/MM/yyyy}", "Date, day first (16/10/2026)"),
        ("{time}", "Time (14:05)"),
        ("{row}", "Spreadsheet row number"),
    ];

    public static bool HasFields(string? text) => text is not null && text.Contains('{') && Fields(text).Any();

    /// <summary>The fields in the text (the part inside the braces), in order.</summary>
    public static IEnumerable<string> Fields(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '{') continue;
            if (i + 1 < text.Length && text[i + 1] == '{') { i++; continue; }
            int end = text.IndexOf('}', i + 1);
            if (end < 0) yield break;
            var inner = text[(i + 1)..end];
            if (inner.Length > 0 && !inner.Contains('{') && !inner.Contains('\n')) yield return inner;
            i = end;
        }
    }

    static (string Key, string? Format) Split(string field)
    {
        int colon = field.IndexOf(':');
        return colon < 0 ? (field.Trim(), null) : (field[..colon].Trim(), field[(colon + 1)..]);
    }

    /// <summary>The value of one field, or null if it isn't one.</summary>
    public static string? Value(string field, VariableData v, DateTime now)
    {
        var (key, format) = Split(field);
        try
        {
            switch (key.ToLowerInvariant())
            {
                case "serial": return v.Serial.ToString(string.IsNullOrEmpty(format) ? "0" : format, CultureInfo.InvariantCulture);
                case "date": return now.ToString(string.IsNullOrEmpty(format) ? "yyyy-MM-dd" : format, CultureInfo.CurrentCulture);
                case "time": return now.ToString(string.IsNullOrEmpty(format) ? "HH:mm" : format, CultureInfo.CurrentCulture);
                case "row": return (v.CsvRow + 1).ToString(string.IsNullOrEmpty(format) ? "0" : format, CultureInfo.InvariantCulture);
            }
        }
        catch (FormatException) { return null; }
        int col = v.Table.Column(field);
        if (col < 0 && format is not null) col = v.Table.Column(key);
        return col < 0 ? null : v.Table.Cell(v.CsvRow, col);
    }

    /// <summary>The text with its fields filled in (unknown fields are left as typed).</summary>
    public static string Resolve(string text, VariableData v, DateTime now)
    {
        var sb = new StringBuilder(text.Length + 16);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '{' && i + 1 < text.Length && text[i + 1] == '{') { sb.Append('{'); i++; continue; }
            if (c == '}' && i + 1 < text.Length && text[i + 1] == '}') { sb.Append('}'); i++; continue; }
            if (c == '{')
            {
                int end = text.IndexOf('}', i + 1);
                if (end > i + 1)
                {
                    var inner = text[(i + 1)..end];
                    if (!inner.Contains('{') && !inner.Contains('\n') && Value(inner, v, now) is { } val)
                    {
                        sb.Append(val);
                        i = end;
                        continue;
                    }
                }
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>The words or content a shape's fields come from, if it has any.</summary>
    static string? Template(Shape s) => s switch
    {
        TextShape t => t.Text,
        CodeShape c => c.Spec.Content,
        _ => null,
    };

    /// <summary>Every field used in the design (lower case keys, e.g. "serial", or a heading).</summary>
    public static IReadOnlySet<string> Used(DocState s)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sh in s.Shapes)
            if (Template(sh) is { } t && t.Contains('{'))
                foreach (var f in Fields(t)) used.Add(Split(f).Key);
        return used;
    }

    static bool UsesCsv(VariableData v, IReadOnlySet<string> used) =>
        used.Contains("row") || used.Any(k => k is not ("serial" or "date" or "time") && v.Table.Column(k) >= 0);

    /// <summary>Fill in every text and code with fields, for this moment. The same state comes back if nothing changed.</summary>
    public static DocState Refresh(DocState s, DateTime now)
    {
        ImmutableList<Shape>.Builder? b = null;
        for (int i = 0; i < s.Shapes.Count; i++)
        {
            Shape? next = s.Shapes[i] switch
            {
                TextShape t => Want(t.Text) is var w && w != t.Shown ? t.WithShown(w) : null,
                CodeShape c => Want(c.Spec.Content) is var w && w != c.Shown ? c.WithShown(w) : null,
                _ => null,
            };
            if (next is null) continue;
            b ??= s.Shapes.ToBuilder();
            b[i] = next;
        }
        return b is null ? s : s with { Shapes = b.ToImmutable() };

        string? Want(string template) => HasFields(template) ? Resolve(template, s.Vars, now) : null;
    }

    /// <summary>
    /// After a finished run: the next serial number and spreadsheet row (when the design uses
    /// them and advancing is on), and the next turn for layers that turn between runs.
    /// </summary>
    public static DocState AfterRun(DocState s, DateTime now)
    {
        var used = Used(s);
        var v = s.Vars;
        if (v.AdvanceAfterRun)
        {
            if (used.Contains("serial")) v = v with { Serial = v.Serial + v.SerialStep };
            if (UsesCsv(v, used)) v = v with { CsvRow = v.CsvRow + 1 };
        }
        var run = JobBuilder.OutputLayers(s).Where(l => l.TurnEachRun && s.ShapesOn(l.Id).Any()).Select(l => l.Id).ToHashSet();
        var layers = run.Count == 0 ? s.Layers : s.Layers.Select(l => run.Contains(l.Id) ? l with { RunCount = l.RunCount + 1 } : l).ToImmutableList();
        if (v == s.Vars && run.Count == 0) return s;
        return Refresh(s with { Vars = v, Layers = layers }, now);
    }

    /// <summary>One line for the job bar: what the next run marks, or null when nothing varies.</summary>
    public static string? Describe(DocState s)
    {
        var used = Used(s);
        if (used.Count == 0) return null;
        var v = s.Vars;
        var parts = new List<string>();
        if (used.Contains("serial")) parts.Add($"serial {v.Serial}");
        if (UsesCsv(v, used) && v.HasCsv) parts.Add($"row {v.CsvRow + 1} of {v.Table.Rows.Length}");
        if (used.Contains("date") || used.Contains("time")) parts.Add("today's date/time");
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    /// <summary>Why the design can't be marked as it is (finished spreadsheet, serial past its end, unknown fields, bad codes).</summary>
    public static IEnumerable<string> Problems(DocState s)
    {
        var used = Used(s);
        var v = s.Vars;
        if (used.Count > 0)
        {
            if (used.Contains("serial") && v.SerialEnd is { } end && (v.SerialStep >= 0 ? v.Serial > end : v.Serial < end))
                yield return $"The serial number {v.Serial} is past the last one ({end}). Change it in Variables.";
            if (UsesCsv(v, used))
            {
                if (!v.HasCsv) yield return "The design uses {row} but no spreadsheet is loaded. Load one in Variables.";
                else if (v.CsvRow >= v.Table.Rows.Length)
                    yield return $"All {v.Table.Rows.Length} rows of {(v.CsvName.Length > 0 ? v.CsvName : "the spreadsheet")} have been marked. Pick a row in Variables to carry on.";
            }
            foreach (var k in used.Where(k => k is not ("serial" or "date" or "time" or "row") && v.Table.Column(k) < 0))
                yield return v.HasCsv
                    ? $"“{{{k}}}” isn't a field: the spreadsheet has no heading “{k}”."
                    : $"“{{{k}}}” isn't a field. Use {{serial}}, {{date}}, {{time}}, or load a spreadsheet in Variables for its headings. For plain braces type {{{{ and }}}}.";
        }
        foreach (var c in s.Shapes.OfType<CodeShape>().Where(c => c.Error is not null))
            yield return $"{CodeShape.KindName(c.Spec.Kind)} “{c.Shown ?? c.Spec.Content}”: {c.Error}";
    }
}
