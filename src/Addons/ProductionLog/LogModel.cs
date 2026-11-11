using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using PhotonCanvas.App;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Addon.ProductionLog;

/// <summary>One item a run marked: its place among the copies, its serial number and spreadsheet row, and what was marked on it.</summary>
public sealed record MarkedItem
{
    /// <summary>0 = the first item of the run; a copy numbered n ahead is item n.</summary>
    public int Index { get; init; }
    /// <summary>The serial number it got (null = the design doesn't use {serial}).</summary>
    public long? Serial { get; init; }
    /// <summary>The spreadsheet row it got, 0 = the first row under the headings (null = no spreadsheet used).</summary>
    public int? Row { get; init; }
    /// <summary>The spreadsheet's name.</summary>
    public string? Sheet { get; init; }
    /// <summary>That row's cells by heading, in the spreadsheet's order.</summary>
    public List<KeyValuePair<string, string>>? RowValues { get; init; }
    /// <summary>What was marked on it: "Text: SN-0042", "QR code: https://…".</summary>
    public List<string> Marks { get; init; } = [];

    /// <summary>"serial 42 · row 12".</summary>
    public string Label()
    {
        var parts = new List<string>();
        if (Serial is { } s) parts.Add($"serial {s}");
        if (Row is { } r) parts.Add($"row {r + 1}");
        return parts.Count == 0 ? $"item {Index + 1}" : string.Join(" · ", parts);
    }

    /// <summary>The row's cells as one line ("Name: Ann, Order: 1234").</summary>
    public string RowText() => RowValues is null ? "" : string.Join(", ", RowValues.Where(kv => kv.Value.Length > 0).Select(kv => $"{kv.Key}: {kv.Value}"));
}

/// <summary>One job sent to the machine, and what became of it.</summary>
public sealed record LoggedRun
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset SentAt { get; init; }
    /// <summary>The design's file name without its extension, or "Untitled".</summary>
    public string Design { get; init; } = "Untitled";
    public string? DesignFile { get; init; }
    /// <summary>Only part of the design: "section 2 of 4", "layer Engrave" (null = all of it).</summary>
    public string? Part { get; init; }
    /// <summary>False for a part with more parts of the same run still to come: its items aren't done until the last part is.</summary>
    public bool CompletesRun { get; init; } = true;
    public List<MarkedItem> Items { get; init; } = [];
    /// <summary>One line per layer sent: name, mode, power, speed, passes, laser.</summary>
    public List<string> Settings { get; init; } = [];
    public double EstimateS { get; init; }
    public double WidthMm { get; init; }
    public double HeightMm { get; init; }
    public string Machine { get; init; } = "";
    /// <summary>Who was signed in to the computer.</summary>
    public string Who { get; init; } = "";

    // What happened (from later lines of the log).
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? EndedAt { get; init; }
    public bool? Finished { get; init; }
    public List<int> Spoiled { get; init; } = [];
    public string Note { get; init; } = "";

    /// <summary>Running time, without the wait for the start button.</summary>
    public double? TookS => StartedAt is { } a && EndedAt is { } b ? Math.Max(0, (b - a).TotalSeconds) : null;

    public bool IsSpoiled(int item) => Spoiled.Contains(item);

    /// <summary>"serials 41–45 · rows 3–7", or "1 piece" when nothing varies.</summary>
    public string ItemsText()
    {
        if (Items.Count == 0) return "no fields";
        var serials = Items.Where(i => i.Serial is not null).Select(i => i.Serial!.Value).ToList();
        var rows = Items.Where(i => i.Row is not null).Select(i => i.Row!.Value + 1).ToList();
        var parts = new List<string>();
        if (serials.Count == 1) parts.Add($"serial {serials[0]}");
        else if (serials.Count > 1) parts.Add($"serials {serials[0]}–{serials[^1]}");
        if (rows.Count == 1) parts.Add($"row {rows[0]}");
        else if (rows.Count > 1) parts.Add($"rows {rows[0]}–{rows[^1]}");
        if (parts.Count == 0) parts.Add(Items.Count == 1 ? "1 item" : $"{Items.Count} items");
        if (Spoiled.Count > 0) parts.Add($"{Spoiled.Count} spoiled");
        return string.Join(" · ", parts);
    }

    /// <summary>Everything a search looks through.</summary>
    public string SearchText() => string.Join("\n", new[] { Design, Part ?? "", Note, Machine, Who, string.Join("\n", Settings) }
        .Concat(Items.SelectMany(i => i.Marks.Append(i.Label()).Append(i.RowText()).Append(i.Serial?.ToString(CultureInfo.InvariantCulture) ?? ""))));
}

/// <summary>Where a run is.</summary>
public enum RunStatus
{
    /// <summary>Uploaded; the machine waits for its start button.</summary>
    Waiting,
    Running,
    Finished,
    /// <summary>Stopped part-way (or ended without finishing).</summary>
    Stopped,
    /// <summary>Never started: another job was sent in its place, or the program closed first.</summary>
    NotRun,
    /// <summary>It started, but no end was recorded (the program closed, or lost the machine, while it ran).</summary>
    Unknown,
}

/// <summary>Turning a sent job into a log entry, and what the log knows about serial numbers and rows.</summary>
public static class LogRuns
{
    static readonly string[] Builtin = ["serial", "date", "time", "row"];

    /// <summary>The design's name in the log: its file name without the extension, or "Untitled".</summary>
    public static string DesignName(string? file) => file is { Length: > 0 } f ? Path.GetFileNameWithoutExtension(f) : "Untitled";

    /// <summary>Does the design use the spreadsheet (its {row} or a heading).</summary>
    public static bool UsesSheet(DocState s)
    {
        var used = VariableText.Used(s);
        return used.Contains("row") || used.Any(k => !Builtin.Contains(k.ToLowerInvariant()) && s.Vars.Table.Column(k) >= 0);
    }

    /// <summary>The items one run of the design marks, with their serial numbers, rows and filled-in text (none when nothing in it varies).</summary>
    public static List<MarkedItem> Items(DocState s)
    {
        var used = VariableText.Used(s);
        if (used.Count == 0) return [];
        bool serial = used.Contains("serial"), sheet = UsesSheet(s);
        var v = s.Vars;
        var sent = new HashSet<Guid>(JobBuilder.OutputLayers(s).SelectMany(s.SentFor).Select(x => x.Id));
        var fielded = s.Shapes.Where(x => sent.Contains(x.Id) && Template(x) is { } t && VariableText.HasFields(t)).ToList();
        int count = VariableText.ItemsPerRun(s);
        var items = new List<MarkedItem>(count);
        for (int i = 0; i < count; i++)
        {
            var at = VariableText.Ahead(v, i);
            List<KeyValuePair<string, string>>? cells = null;
            if (sheet && v.HasCsv && at.CsvRow >= 0 && at.CsvRow < v.Table.Rows.Length)
                cells = [.. v.Table.Headers.Select((h, c) => new KeyValuePair<string, string>(h.Trim(), v.Table.Cell(at.CsvRow, c)))];
            items.Add(new MarkedItem
            {
                Index = i,
                Serial = serial ? at.Serial : null,
                Row = sheet ? at.CsvRow : null,
                Sheet = sheet ? (v.CsvName.Length > 0 ? v.CsvName : "spreadsheet") : null,
                RowValues = cells,
                Marks = [.. fielded.Where(x => x.VarOffset == i).Select(Mark)],
            });
        }
        return items;
    }

    static string? Template(Shape s) => s switch
    {
        TextShape t => t.Text,
        CodeShape c => c.Spec.Content,
        _ => null,
    };

    static string Mark(Shape s) => s switch
    {
        TextShape t => $"Text: {(t.Shown ?? t.Text).ReplaceLineEndings(" / ")}",
        CodeShape c => $"{CodeShape.KindName(c.Spec.Kind)}: {(c.Shown ?? c.Spec.Content).ReplaceLineEndings(" / ")}",
        _ => "",
    };

    /// <summary>The settings of each layer sent, one line each (a summary past 12 layers).</summary>
    public static List<string> Settings(DocState s)
    {
        var sent = JobBuilder.OutputLayers(s).Where(l => s.SentFor(l).Any()).ToList();
        static string Line(Layer l)
        {
            var laser = l.Laser.Light == LightSource.Red ? $"MOPA {l.Laser.PulseWidthNs} ns {l.Laser.FrequencyKhz} kHz" : "diode";
            var material = l.Material is { } m ? $", {m.Label}{(m.RecipeTitle is { Length: > 0 } r ? $" – {r}" : "")}" : "";
            return $"{l.Name}: {l.Mode}, {l.Laser.Power:0.#}% at {l.Laser.Speed:0.#} mm/s × {l.Laser.Passes} ({laser}){material}";
        }
        if (sent.Count <= 12) return [.. sent.Select(Line)];
        return [$"{sent.Count} layers", $"First: {Line(sent[0])}", $"Last: {Line(sent[^1])}"];
    }

    /// <summary>The log entry for a job just uploaded.</summary>
    public static LoggedRun From(DocState state, string? designFile, DateTimeOffset sentAt, string? part, bool completesRun,
        double estimateS, Rect2 bounds, string machine, string who) => new()
    {
        SentAt = sentAt,
        Design = DesignName(designFile),
        DesignFile = designFile,
        Part = part,
        CompletesRun = completesRun,
        Items = Items(state),
        Settings = Settings(state),
        EstimateS = estimateS,
        WidthMm = bounds.Width,
        HeightMm = bounds.Height,
        Machine = machine,
        Who = who,
    };

    /// <summary>Where a run is. <paramref name="liveId"/> is the run the machine has now (waiting or running), if any.</summary>
    public static RunStatus Status(LoggedRun r, string? liveId) =>
        r.EndedAt is not null ? (r.Finished == true ? RunStatus.Finished : RunStatus.Stopped)
        : r.StartedAt is not null ? (r.Id == liveId ? RunStatus.Running : RunStatus.Unknown)
        : r.Id == liveId ? RunStatus.Waiting : RunStatus.NotRun;

    public static string StatusText(RunStatus s) => s switch
    {
        RunStatus.Waiting => "Waiting for start",
        RunStatus.Running => "Running",
        RunStatus.Finished => "Finished",
        RunStatus.Stopped => "Stopped",
        RunStatus.NotRun => "Not run",
        _ => "No end recorded",
    };

    /// <summary>A run whose items count as marked: it finished, and it was the whole run (or its last part).</summary>
    public static bool Done(LoggedRun r) => r.Finished == true && r.CompletesRun;

    static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    static string Key(MarkedItem i) => i.Sheet + "\u001f" + string.Join("\u001f", i.RowValues?.Select(kv => kv.Value) ?? []);

    /// <summary>
    /// Warnings for items about to be marked that the log says were marked already (finished, not
    /// spoiled) for the same design: the same serial number, or the same spreadsheet row.
    /// </summary>
    public static List<string> AlreadyMarked(IEnumerable<LoggedRun> log, string design, IReadOnlyList<MarkedItem> next)
    {
        var warnings = new List<string>();
        if (next.Count == 0 || Same(design, "Untitled")) return warnings;
        var done = log.Where(r => Same(r.Design, design) && Done(r))
            .SelectMany(r => r.Items.Where(i => !r.IsSpoiled(i.Index)).Select(i => (Run: r, Item: i))).ToList();
        if (done.Count == 0) return warnings;

        var serials = new Dictionary<long, LoggedRun>();
        foreach (var (r, i) in done)
            if (i.Serial is { } s && (!serials.TryGetValue(s, out var had) || had.SentAt < r.SentAt)) serials[s] = r;
        var again = next.Where(i => i.Serial is { } s && serials.ContainsKey(s)).Select(i => i.Serial!.Value).ToList();
        if (again.Count == 1)
            warnings.Add($"serial {again[0]} was already marked ({When(serials[again[0]])}).");
        else if (again.Count > 1)
            warnings.Add($"{again.Count} of the serial numbers this run marks ({again.Min()}–{again.Max()}) were already marked, the last {When(again.Select(s => serials[s]).MaxBy(r => r.SentAt)!)}.");

        var rows = new Dictionary<string, LoggedRun>();
        foreach (var (r, i) in done)
            if (i.RowValues is { Count: > 0 } && (!rows.TryGetValue(Key(i), out var had) || had.SentAt < r.SentAt)) rows[Key(i)] = r;
        var rowAgain = next.Where(i => i.RowValues is { Count: > 0 } && rows.ContainsKey(Key(i))).ToList();
        if (rowAgain.Count == 1)
            warnings.Add($"spreadsheet row {rowAgain[0].Row + 1} ({Short(rowAgain[0].RowText())}) was already marked ({When(rows[Key(rowAgain[0])])}).");
        else if (rowAgain.Count > 1)
            warnings.Add($"{rowAgain.Count} of the spreadsheet rows this run marks (rows {rowAgain.Min(i => i.Row) + 1}–{rowAgain.Max(i => i.Row) + 1}) were already marked, the last {When(rowAgain.Select(i => rows[Key(i)]).MaxBy(r => r.SentAt)!)}.");
        if (warnings.Count > 0)
            warnings.Add("Check the pieces, or use Carry on in the production log (Machine → Job → Production log) to start after the last one marked.");
        return warnings;
    }

    static string Short(string s) => s.Length <= 40 ? s : s[..39] + "…";

    static string When(LoggedRun r) => $"on {(r.EndedAt ?? r.SentAt).ToLocalTime():d MMM yyyy} at {(r.EndedAt ?? r.SentAt).ToLocalTime():HH:mm}";

    /// <summary>
    /// The serial number and row that carry on after the design's last finished run in the log (null
    /// when the log has none that marked a serial or row), and a line saying what that run was.
    /// </summary>
    public static (VariableData Vars, string From)? CarryOn(IEnumerable<LoggedRun> log, string design, VariableData now)
    {
        var last = log.Where(r => Same(r.Design, design) && Done(r) && r.Items.Any(i => i.Serial is not null || i.Row is not null))
            .MaxBy(r => r.EndedAt ?? r.SentAt);
        if (last is null) return null;
        var item = last.Items[^1];
        var v = now;
        if (item.Serial is { } s) v = v with { Serial = s + (now.SerialStep == 0 ? 1 : now.SerialStep) };
        if (item.Row is { } row) v = v with { CsvRow = row + 1 };
        return (v, $"the run that finished {When(last)} ({last.ItemsText()})");
    }

    /// <summary>The design's serial number and row set back to an item, to mark it again.</summary>
    public static VariableData GoBackTo(MarkedItem item, VariableData now)
    {
        var v = now;
        if (item.Serial is { } s) v = v with { Serial = s };
        if (item.Row is { } r) v = v with { CsvRow = r };
        return v;
    }

    /// <summary>The log as a spreadsheet: one line per item (one per run when nothing varied), oldest first.</summary>
    public static string Csv(IEnumerable<LoggedRun> runs, string? liveId)
    {
        var sb = new StringBuilder();
        void Line(params string[] cells) => sb.AppendLine(string.Join(",", cells.Select(Quote)));
        Line("Sent", "Started", "Ended", "Status", "Design", "Part", "Item", "Serial", "Spreadsheet", "Row", "Row values", "Marked", "Spoiled", "Took (s)", "Machine", "Who", "Settings", "Note");
        foreach (var r in runs.OrderBy(r => r.SentAt))
        {
            string[] common(MarkedItem? i) =>
            [
                Stamp(r.SentAt), r.StartedAt is { } a ? Stamp(a) : "", r.EndedAt is { } e ? Stamp(e) : "", StatusText(Status(r, liveId)),
                r.Design, r.Part ?? "", i is null ? "" : (i.Index + 1).ToString(CultureInfo.InvariantCulture),
                i?.Serial?.ToString(CultureInfo.InvariantCulture) ?? "", i?.Sheet ?? "", i?.Row is { } row ? (row + 1).ToString(CultureInfo.InvariantCulture) : "",
                i?.RowText() ?? "", i is null ? "" : string.Join(" | ", i.Marks), i is not null && r.IsSpoiled(i.Index) ? "yes" : "",
                r.TookS is { } t ? t.ToString("0", CultureInfo.InvariantCulture) : "", r.Machine, r.Who, string.Join(" | ", r.Settings), r.Note,
            ];
            if (r.Items.Count == 0) Line(common(null));
            else foreach (var i in r.Items) Line(common(i));
        }
        return sb.ToString();

        static string Stamp(DateTimeOffset t) => t.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        static string Quote(string s) => s.IndexOfAny([',', '"', '\n', '\r']) < 0 ? s : "\"" + s.Replace("\"", "\"\"") + "\"";
    }
}

/// <summary>
/// The log on disk: one line of JSON per event (a job sent, started, ended; an item marked spoiled; a
/// note), only ever appended, so a crash loses at most the line being written and never the rest.
/// Safe to read from any thread.
/// </summary>
public sealed class LogBook
{
    readonly string _path;
    readonly object _gate = new();
    List<LoggedRun> _runs = [];
    IReadOnlyList<LoggedRun> _snapshot = [];
    // The file's last line was cut off (a crash while writing it): the next line starts on a line of its own.
    bool _cutOff;

    static readonly JsonSerializerOptions Json = new()
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>One line of the file.</summary>
    sealed record Line
    {
        public string T { get; init; } = "";
        public string Id { get; init; } = "";
        public DateTimeOffset? At { get; init; }
        public LoggedRun? Run { get; init; }
        public bool? Finished { get; init; }
        public int? Item { get; init; }
        public bool? On { get; init; }
        public string? Text { get; init; }
    }

    LogBook(string path) => _path = path;

    public string FilePath => _path;

    /// <summary>The run the machine has now (sent and waiting, or running), if it's in the log.</summary>
    public string? LiveId { get; set; }

    /// <summary>Raised (on the thread that changed it) after anything is added.</summary>
    public event Action? Changed;

    /// <summary>Every run, oldest first.</summary>
    public IReadOnlyList<LoggedRun> Runs => _snapshot;

    public LoggedRun? Find(string id) => _snapshot.FirstOrDefault(r => r.Id == id);

    /// <summary>Read the log (an empty one when the file isn't there or can't be read; bad lines are skipped).</summary>
    public static LogBook Open(string path)
    {
        var book = new LogBook(path);
        try
        {
            if (File.Exists(path))
            {
                using (var f = File.OpenRead(path))
                    if (f.Length > 0)
                    {
                        f.Seek(-1, SeekOrigin.End);
                        book._cutOff = f.ReadByte() != '\n';
                    }
                foreach (var text in File.ReadLines(path))
                {
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    try { if (JsonSerializer.Deserialize<Line>(text, Json) is { } line) book.Apply(line); }
                    catch (JsonException) { }
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        book._snapshot = [.. book._runs];
        return book;
    }

    public LoggedRun Add(LoggedRun run)
    {
        Write(new Line { T = "sent", Id = run.Id, Run = run });
        return run;
    }

    public void Started(string id, DateTimeOffset at) => Write(new Line { T = "start", Id = id, At = at });

    public void Ended(string id, DateTimeOffset at, bool finished) => Write(new Line { T = "end", Id = id, At = at, Finished = finished });

    public void SetSpoiled(string id, int item, bool on) => Write(new Line { T = "spoiled", Id = id, Item = item, On = on });

    public void SetNote(string id, string note) => Write(new Line { T = "note", Id = id, Text = note });

    void Write(Line line)
    {
        lock (_gate)
        {
            if (!Apply(line)) return;
            _snapshot = [.. _runs];
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.AppendAllText(_path, (_cutOff ? "\n" : "") + JsonSerializer.Serialize(line, Json) + "\n");
                _cutOff = false;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { AppLog.Error("Production log", e); }
        }
        Changed?.Invoke();
    }

    bool Apply(Line line)
    {
        if (line.T == "sent")
        {
            if (line.Run is null || _runs.Any(r => r.Id == line.Run.Id)) return false;
            _runs.Add(line.Run with { Id = line.Id.Length > 0 ? line.Id : line.Run.Id });
            return true;
        }
        int at = _runs.FindIndex(r => r.Id == line.Id);
        if (at < 0) return false;
        var r = _runs[at];
        _runs[at] = line.T switch
        {
            "start" => r with { StartedAt = line.At },
            "end" => r with { EndedAt = line.At, Finished = line.Finished, StartedAt = r.StartedAt ?? line.At },
            "spoiled" when line.Item is { } i => r with { Spoiled = line.On == true ? [.. r.Spoiled.Where(x => x != i).Append(i).Order()] : [.. r.Spoiled.Where(x => x != i)] },
            "note" => r with { Note = line.Text ?? "" },
            _ => r,
        };
        return true;
    }
}

/// <summary>The log and its settings, kept in a folder (the add-on's data folder).</summary>
public sealed class LogStore
{
    readonly string _settingsPath;
    LogSettings _settings;

    public LogStore(string folder)
    {
        Book = LogBook.Open(Path.Combine(folder, "production-log.jsonl"));
        _settingsPath = Path.Combine(folder, "settings.json");
        _settings = LogSettings.Load(_settingsPath);
    }

    public LogBook Book { get; }

    /// <summary>The settings; setting them saves them.</summary>
    public LogSettings Settings
    {
        get => _settings;
        set
        {
            _settings = value;
            value.Save(_settingsPath);
        }
    }
}

/// <summary>The production log's own settings (kept in its data folder).</summary>
public sealed record LogSettings
{
    /// <summary>Warn in the job's checks when a serial number or spreadsheet row was already marked.</summary>
    public bool WarnAgain { get; init; } = true;

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, TypeInfoResolver = new DefaultJsonTypeInfoResolver() };

    public static LogSettings Load(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<LogSettings>(File.ReadAllText(path), Json) ?? new() : new(); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }

    public void Save(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, Json));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { AppLog.Error("Production log", e); }
    }
}
