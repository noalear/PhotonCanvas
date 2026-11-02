using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Addon.Quote;

/// <summary>What a sheet of a material costs, and how big the sheet is (mm).</summary>
public sealed record SheetPrice(double Price, double WidthMm, double HeightMm)
{
    public double AreaMm2 => Math.Max(0, WidthMm) * Math.Max(0, HeightMm);
}

/// <summary>
/// Your rates and business details: the same for every quote, kept in the add-on's data folder
/// (rates.json). Prices are in your own currency; nothing here is converted.
/// </summary>
public sealed class QuoteRates
{
    public string Currency { get; set; } = "$";
    /// <summary>US Letter paper for the printed quote; false = A4.</summary>
    public bool Letter { get; set; } = true;
    /// <summary>Running the machine, per hour of laser time: its wear, the laser source's life, paying it off.</summary>
    public double MachinePerHour { get; set; } = 15;
    /// <summary>What the machine, exhaust and purifier draw together while a job runs (W). A rough guess to start with.</summary>
    public double PowerW { get; set; } = 350;
    public double PerKwh { get; set; } = 0.16;
    public double LabourPerHour { get; set; } = 20;
    /// <summary>Minutes of your time once per order: setting up the design, a test, talking to the customer.</summary>
    public double SetupMin { get; set; } = 10;
    /// <summary>Minutes of your time each run: putting the material in, taking the pieces out, cleaning them.</summary>
    public double HandlingMin { get; set; } = 2;
    /// <summary>Added on top of the material that's used: offcuts, edges, a spoiled piece now and then (%).</summary>
    public double WastePct { get; set; } = 15;
    public double MarkupPct { get; set; } = 30;
    /// <summary>The least you charge for an order (0 = no minimum).</summary>
    public double Minimum { get; set; }
    /// <summary>The price is rounded up to a multiple of this (0 = to the cent).</summary>
    public double RoundTo { get; set; } = 0.5;
    public double TaxPct { get; set; }
    public string TaxName { get; set; } = "Tax";
    public string Business { get; set; } = "";
    public string Contact { get; set; } = "";
    public string Note { get; set; } = "Prices are valid for 30 days.";
    /// <summary>Show the customer what the price is made of (material, laser time, labour, extras), not just one line.</summary>
    public bool ShowBreakdown { get; set; }
    /// <summary>The number the next quote gets.</summary>
    public int NextNumber { get; set; } = 1;
    /// <summary>Sheet prices by material: the library material's id ("N" format), or <see cref="QuoteMath.OtherKey"/>.</summary>
    public Dictionary<string, SheetPrice> Sheets { get; set; } = [];

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>The rates saved at <paramref name="path"/>; the defaults when there are none or the file is damaged.</summary>
    public static QuoteRates Load(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<QuoteRates>(File.ReadAllText(path), Json) ?? new() : new(); }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or NotSupportedException) { return new(); }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
        File.Move(tmp, path, overwrite: true);
    }

    public QuoteRates Copy() => JsonSerializer.Deserialize<QuoteRates>(JsonSerializer.Serialize(this, Json), Json)!;
}

/// <summary>Something else charged on the quote: a box, a stand, masking, design work.</summary>
public sealed record ExtraLine(string Name, double Amount, bool EachRun);

/// <summary>Where the run's time comes from.</summary>
public enum TimeSource
{
    /// <summary>The program's estimate, corrected by what past jobs showed.</summary>
    Estimate,
    /// <summary>How long this design really took the last time it finished.</summary>
    LastRun,
}

/// <summary>This design's quote: who it's for and how many. Kept in the design (DocState.AddonData "quote.job").</summary>
public sealed record QuoteJob
{
    public const string Key = "quote.job";

    /// <summary>What the quote calls the job; empty = the design's file name.</summary>
    public string Title { get; init; } = "";
    public string Customer { get; init; } = "";
    /// <summary>How many times the job is run.</summary>
    public int Runs { get; init; } = 1;
    /// <summary>How many finished pieces each run makes (for the price of one).</summary>
    public int PiecesPerRun { get; init; } = 1;
    /// <summary>The customer brings the material: none is charged.</summary>
    public bool CustomerMaterial { get; init; }
    /// <summary>Charge whole sheets (rounded up), not just the part used.</summary>
    public bool WholeSheets { get; init; }
    public TimeSource Time { get; init; }
    public IReadOnlyList<ExtraLine> Extras { get; init; } = [];

    static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>The quote kept in the design, or a new one.</summary>
    public static QuoteJob From(DocState s)
    {
        if (!s.AddonData.TryGetValue(Key, out var json)) return new();
        try { return JsonSerializer.Deserialize<QuoteJob>(json, Json) ?? new(); }
        catch (JsonException) { return new(); }
    }
}

/// <summary>One material the job uses, and how much of it one run takes (the box around its parts, mm²).</summary>
public sealed record MaterialUse(string Key, string Label, double AreaMm2, double WidthMm, double HeightMm);

/// <summary>One line of what the job costs you.</summary>
public enum CostKind { Material, Machine, Power, Labour, Extra }

public sealed record CostLine(CostKind Kind, string Name, string Detail, double Amount);

/// <summary>A line the customer sees on the quote.</summary>
public sealed record PriceLine(string Name, string Detail, double Amount);

/// <summary>What the job costs, what to charge, and what that leaves.</summary>
public sealed record QuoteResult(
    IReadOnlyList<CostLine> Costs,
    double Cost,
    double Subtotal,
    double Tax,
    double Total,
    double PerPiece,
    int Pieces,
    double RunSeconds,
    IReadOnlyList<PriceLine> Lines,
    IReadOnlyList<string> Notes)
{
    /// <summary>What's left after the costs (before tax).</summary>
    public double Profit => Subtotal - Cost;
    /// <summary>Laser time for the whole order (hours).</summary>
    public double LaserHours => RunSeconds * Math.Max(1, Runs) / 3600;
    public int Runs { get; init; } = 1;
}

public static class QuoteMath
{
    /// <summary>The sheet price used for layers that have no library material.</summary>
    public const string OtherKey = "other";

    /// <summary>
    /// The materials a design uses, from its layers that go to the laser: each library material once
    /// (layers without one count as one "Material"), with the box around all its parts.
    /// </summary>
    public static List<MaterialUse> Materials(DocState s)
    {
        var boxes = new Dictionary<string, (string Label, Rect2 Box)>();
        foreach (var l in JobBuilder.OutputLayers(s))
        {
            var box = s.ShapesFor(l).Select(sh => sh.Bounds).Where(b => !b.IsEmpty).Aggregate(Rect2.Empty, (a, b) => a.Union(b));
            if (box.IsEmpty) continue;
            string key = l.Material is { } m ? m.MaterialId.ToString("N") : OtherKey;
            string label = l.Material is { Label.Length: > 0 } m2 ? m2.Label : "Material";
            boxes[key] = boxes.TryGetValue(key, out var have) ? (have.Label, have.Box.Union(box)) : (label, box);
        }
        return [.. boxes.Select(kv => new MaterialUse(kv.Key, kv.Value.Label, kv.Value.Box.Width * kv.Value.Box.Height, kv.Value.Box.Width, kv.Value.Box.Height))
            .OrderBy(m => m.Key == OtherKey).ThenBy(m => m.Label, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>
    /// The time a finished run of this design really took last time: the newest finished job of
    /// the same name whose estimate was within 15% of <paramref name="estimateS"/> (so an edited
    /// design doesn't use an old time).
    /// </summary>
    public static double? LastRun(IEnumerable<JobRecord> history, string design, double estimateS) =>
        history.Where(r => r.Finished && r.TookS > 0 && string.Equals(r.Design, design, StringComparison.OrdinalIgnoreCase)
                && r.EstimateS > 0 && Math.Abs(r.EstimateS - estimateS) <= 0.15 * Math.Max(estimateS, 1))
            .OrderByDescending(r => r.When).Select(r => (double?)r.TookS).FirstOrDefault();

    /// <summary>Rounds up to a multiple of <paramref name="step"/> (cents when it's 0).</summary>
    public static double RoundUp(double v, double step)
    {
        if (step <= 0) step = 0.01;
        return Math.Round(Math.Ceiling(v / step - 1e-9) * step, 2);
    }

    /// <summary>
    /// The quote: costs line by line, then the price (costs plus markup, at least the minimum,
    /// rounded up), the tax on it, and what one piece comes to.
    /// </summary>
    /// <param name="runSeconds">How long one run takes on the machine.</param>
    public static QuoteResult Price(double runSeconds, IReadOnlyList<MaterialUse> materials, QuoteJob job, QuoteRates r, string title)
    {
        int runs = Math.Max(1, job.Runs);
        int pieces = runs * Math.Max(1, job.PiecesPerRun);
        var costs = new List<CostLine>();
        var notes = new List<string>();
        string Money(double v) => Format(v, r.Currency);

        // Material.
        if (job.CustomerMaterial)
            costs.Add(new(CostKind.Material, "Material", "the customer's own", 0));
        else
            foreach (var m in materials)
            {
                double used = m.AreaMm2 * runs * (1 + Math.Max(0, r.WastePct) / 100);
                if (!r.Sheets.TryGetValue(m.Key, out var sheet) || sheet.AreaMm2 <= 0 || sheet.Price <= 0)
                {
                    costs.Add(new(CostKind.Material, m.Label, "no sheet price set", 0));
                    notes.Add($"{m.Label} has no sheet price yet, so it's charged at nothing.");
                    continue;
                }
                bool fits = m.WidthMm <= sheet.WidthMm + 0.01 && m.HeightMm <= sheet.HeightMm + 0.01
                    || m.WidthMm <= sheet.HeightMm + 0.01 && m.HeightMm <= sheet.WidthMm + 0.01;
                if (!fits)
                    notes.Add($"The parts on {m.Label} ({m.WidthMm:0} × {m.HeightMm:0} mm) are bigger than its sheet ({sheet.WidthMm:0} × {sheet.HeightMm:0} mm).");
                double sheets = used / sheet.AreaMm2;
                if (job.WholeSheets)
                {
                    int whole = Math.Max(1, (int)Math.Ceiling(sheets - 1e-9));
                    costs.Add(new(CostKind.Material, m.Label, $"{whole} sheet{(whole == 1 ? "" : "s")} at {Money(sheet.Price)}", whole * sheet.Price));
                }
                else
                    costs.Add(new(CostKind.Material, m.Label, $"{used / 100:0.#} cm² ({sheets * 100:0.#}% of a {Money(sheet.Price)} sheet)", sheets * sheet.Price));
            }

        // The machine and its electricity.
        double hours = Math.Max(0, runSeconds) * runs / 3600;
        string time = TimeText.Duration(Math.Max(0, runSeconds) * runs);
        costs.Add(new(CostKind.Machine, "Laser time", $"{time} at {Money(r.MachinePerHour)} an hour", hours * Math.Max(0, r.MachinePerHour)));
        double kwh = hours * Math.Max(0, r.PowerW) / 1000;
        costs.Add(new(CostKind.Power, "Electricity", $"{kwh:0.##} kWh at {Money(r.PerKwh)}", kwh * Math.Max(0, r.PerKwh)));

        // Your time.
        double minutes = Math.Max(0, r.SetupMin) + Math.Max(0, r.HandlingMin) * runs;
        costs.Add(new(CostKind.Labour, "Your time", $"{minutes:0.#} min at {Money(r.LabourPerHour)} an hour", minutes / 60 * Math.Max(0, r.LabourPerHour)));

        foreach (var e in job.Extras.Where(e => !string.IsNullOrWhiteSpace(e.Name) || e.Amount != 0))
        {
            string name = string.IsNullOrWhiteSpace(e.Name) ? "Extra" : e.Name.Trim();
            costs.Add(e.EachRun && runs > 1
                ? new(CostKind.Extra, name, $"{runs} × {Money(e.Amount)}", e.Amount * runs)
                : new(CostKind.Extra, name, "", e.Amount));
        }

        double cost = costs.Sum(c => c.Amount);
        double marked = cost * (1 + r.MarkupPct / 100);
        double subtotal = RoundUp(Math.Max(Math.Max(0, marked), Math.Max(0, r.Minimum)), r.RoundTo);
        if (r.Minimum > 0 && marked < r.Minimum) notes.Add($"Charged the minimum of {Money(r.Minimum)}.");
        double tax = Math.Round(subtotal * Math.Max(0, r.TaxPct) / 100, 2);

        // What the customer sees: one line, or the costs scaled so they add up to the price.
        var lines = new List<PriceLine>();
        string what = string.IsNullOrWhiteSpace(title) ? "Laser job" : title.Trim();
        if (!r.ShowBreakdown || cost <= 0)
            lines.Add(new(runs == 1 ? what : $"{what} × {runs}", pieces > 1 ? $"{pieces} pieces" : "", subtotal));
        else
        {
            double k = subtotal / cost;
            void Add(string name, string detail, double amount) { if (amount > 0.004) lines.Add(new(name, detail, amount)); }
            Add("Material", job.CustomerMaterial ? "" : string.Join(", ", costs.Where(c => c.Kind == CostKind.Material && c.Amount > 0).Select(c => c.Name)),
                costs.Where(c => c.Kind == CostKind.Material).Sum(c => c.Amount) * k);
            Add("Laser time", time, costs.Where(c => c.Kind is CostKind.Machine or CostKind.Power).Sum(c => c.Amount) * k);
            Add("Set-up and handling", "", costs.Where(c => c.Kind == CostKind.Labour).Sum(c => c.Amount) * k);
            foreach (var c in costs.Where(c => c.Kind == CostKind.Extra)) Add(c.Name, "", c.Amount * k);
            // Rounding the parts to cents can leave a cent over or under: the biggest line takes it.
            var rounded = lines.Select(l => l with { Amount = Math.Round(l.Amount, 2) }).ToList();
            double off = Math.Round(subtotal - rounded.Sum(l => l.Amount), 2);
            if (rounded.Count > 0 && off != 0)
            {
                int big = rounded.IndexOf(rounded.MaxBy(l => l.Amount)!);
                rounded[big] = rounded[big] with { Amount = rounded[big].Amount + off };
            }
            lines = rounded;
        }
        return new QuoteResult(costs, cost, subtotal, tax, subtotal + tax, (subtotal + tax) / pieces, pieces, Math.Max(0, runSeconds), lines, notes) { Runs = runs };
    }

    /// <summary>"$12.50" (the symbol before the amount, two decimals, in the current culture's digits).</summary>
    public static string Format(double v, string currency) =>
        (v < 0 ? "-" : "") + currency + Math.Abs(v).ToString("#,0.00", CultureInfo.CurrentCulture);

    /// <summary>The quote as plain text, for an email or a message.</summary>
    public static string Text(QuoteResult q, QuoteJob job, QuoteRates r, string title, int number, DateTime date)
    {
        var sb = new StringBuilder();
        if (r.Business.Length > 0) sb.AppendLine(r.Business.Trim());
        if (r.Contact.Length > 0) sb.AppendLine(r.Contact.Trim());
        if (sb.Length > 0) sb.AppendLine();
        sb.AppendLine($"Quote {NumberText(number)} · {date:d}");
        if (job.Customer.Length > 0) sb.AppendLine($"For: {job.Customer.Trim()}");
        sb.AppendLine();
        foreach (var l in q.Lines)
            sb.AppendLine($"{l.Name}{(l.Detail.Length > 0 ? $" ({l.Detail})" : "")}: {Format(l.Amount, r.Currency)}");
        if (q.Tax > 0)
        {
            sb.AppendLine($"Subtotal: {Format(q.Subtotal, r.Currency)}");
            sb.AppendLine($"{TaxLabel(r)}: {Format(q.Tax, r.Currency)}");
        }
        sb.AppendLine($"Total: {Format(q.Total, r.Currency)}");
        if (q.Pieces > 1) sb.AppendLine($"That's {Format(q.PerPiece, r.Currency)} a piece for {q.Pieces} pieces.");
        if (r.Note.Trim().Length > 0) { sb.AppendLine(); sb.AppendLine(r.Note.Trim()); }
        return sb.ToString();
    }

    public static string NumberText(int number) => $"Q-{Math.Max(1, number):0000}";

    public static string TaxLabel(QuoteRates r) => $"{(string.IsNullOrWhiteSpace(r.TaxName) ? "Tax" : r.TaxName.Trim())} ({r.TaxPct:0.##}%)";
}
