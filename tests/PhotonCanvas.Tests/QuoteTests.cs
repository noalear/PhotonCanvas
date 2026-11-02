using PhotonCanvas.Addon.Quote;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Materials;
using Xunit;

namespace PhotonCanvas.Tests;

/// <summary>The Quote and cost add-on: its sums, the materials it finds, and the quote it writes.</summary>
public class QuoteTests
{
    static readonly Guid Ply = Guid.NewGuid();

    static PathShape Box(Guid layer, double x, double y, double w, double h) => new() { LayerId = layer, Geometry = PathGeometry.Rect(x, y, w, h) };

    static QuoteRates Rates() => new()
    {
        MachinePerHour = 12,
        PowerW = 500,
        PerKwh = 0.20,
        LabourPerHour = 30,
        SetupMin = 10,
        HandlingMin = 2,
        WastePct = 10,
        MarkupPct = 50,
        RoundTo = 1,
        TaxPct = 10,
        Sheets = { [Ply.ToString("N")] = new SheetPrice(9, 300, 300) },
    };

    static readonly MaterialUse PlyUse = new(Ply.ToString("N"), "Plywood 3 mm", 5000, 100, 50);

    [Fact]
    public void ThePriceIsTheCostsMarkedUpRoundedAndTaxed()
    {
        var job = new QuoteJob { Runs = 3, PiecesPerRun = 2, Extras = [new("Gift box", 1.5, true), new("Design work", 5, false)] };
        var q = QuoteMath.Price(600, [PlyUse], job, Rates(), "Coasters");

        double Cost(CostKind k) => q.Costs.Where(c => c.Kind == k).Sum(c => c.Amount);
        Assert.Equal(5000 * 3 * 1.1 / 90000 * 9, Cost(CostKind.Material), 6); // 1.65: the parts' box × runs + 10% waste, as a share of the sheet
        Assert.Equal(6, Cost(CostKind.Machine), 6);    // half an hour at 12
        Assert.Equal(0.05, Cost(CostKind.Power), 6);   // 0.25 kWh at 0.20
        Assert.Equal(8, Cost(CostKind.Labour), 6);     // 10 + 3 × 2 min at 30 an hour
        Assert.Equal(4.5 + 5, Cost(CostKind.Extra), 6);
        Assert.Equal(25.20, q.Cost, 6);
        Assert.Equal(38, q.Subtotal, 6);               // 37.80 rounded up to a whole one
        Assert.Equal(3.80, q.Tax, 6);
        Assert.Equal(41.80, q.Total, 6);
        Assert.Equal(6, q.Pieces);
        Assert.Equal(41.80 / 6, q.PerPiece, 6);
        Assert.Equal(38 - 25.20, q.Profit, 6);
        Assert.Equal(0.5, q.LaserHours, 6);
        // Without the breakdown the customer sees one line.
        var line = Assert.Single(q.Lines);
        Assert.Equal("Coasters × 3", line.Name);
        Assert.Equal(38, line.Amount, 6);
    }

    [Fact]
    public void TheBreakdownAddsUpToThePrice()
    {
        var r = Rates();
        r.ShowBreakdown = true;
        r.RoundTo = 0.25;
        var job = new QuoteJob { Runs = 7, Extras = [new("Stand", 2.37, true)] };
        var q = QuoteMath.Price(431, [PlyUse], job, r, "Sign");
        Assert.Equal(["Material", "Laser time", "Set-up and handling", "Stand"], q.Lines.Select(l => l.Name));
        Assert.Equal(q.Subtotal, q.Lines.Sum(l => l.Amount), 6);
        Assert.All(q.Lines, l => Assert.Equal(Math.Round(l.Amount, 2), l.Amount, 9));
    }

    [Fact]
    public void WholeSheetsTheCustomersMaterialAndTheMinimum()
    {
        var r = Rates();
        var big = PlyUse with { AreaMm2 = 60000, WidthMm = 300, HeightMm = 200 };
        // 2 runs × 60 000 mm² + 10% = 132 000 mm²: two 300 × 300 sheets started.
        var whole = QuoteMath.Price(0, [big], new QuoteJob { Runs = 2, WholeSheets = true }, r, "");
        var sheets = Assert.Single(whole.Costs, c => c.Kind == CostKind.Material);
        Assert.Equal(18, sheets.Amount, 6);
        Assert.StartsWith("2 sheets", sheets.Detail);

        var own = QuoteMath.Price(0, [big], new QuoteJob { CustomerMaterial = true }, r, "");
        Assert.Equal(0, own.Costs.Where(c => c.Kind == CostKind.Material).Sum(c => c.Amount));

        r.Minimum = 25;
        var small = QuoteMath.Price(10, [PlyUse], new QuoteJob(), r, "Tag");
        Assert.Equal(25, small.Subtotal, 6);
        Assert.Contains(small.Notes, n => n.Contains("minimum"));
    }

    [Fact]
    public void AMaterialWithNoPriceOrTooSmallASheetIsPointedOut()
    {
        var r = Rates();
        var none = QuoteMath.Price(60, [new MaterialUse("other", "Material", 100, 10, 10)], new QuoteJob(), r, "");
        Assert.Contains(none.Notes, n => n.Contains("no sheet price"));
        var wide = QuoteMath.Price(60, [PlyUse with { WidthMm = 400, HeightMm = 20 }], new QuoteJob(), r, "");
        Assert.Contains(wide.Notes, n => n.Contains("bigger than its sheet"));
        // Turned, it fits: no note.
        r.Sheets[Ply.ToString("N")] = new SheetPrice(9, 100, 600);
        var turned = QuoteMath.Price(60, [PlyUse with { WidthMm = 400, HeightMm = 20 }], new QuoteJob(), r, "");
        Assert.DoesNotContain(turned.Notes, n => n.Contains("bigger"));
    }

    [Fact]
    public void RoundingUpKeepsExactAmounts()
    {
        Assert.Equal(10, QuoteMath.RoundUp(10, 0.5));
        Assert.Equal(10.5, QuoteMath.RoundUp(10.01, 0.5));
        Assert.Equal(0.3, QuoteMath.RoundUp(0.1 + 0.2, 0.1)); // 0.30000000000000004 stays 0.3
        Assert.Equal(12.35, QuoteMath.RoundUp(12.341, 0));    // 0 = to the cent
    }

    [Fact]
    public void MaterialsComeFromTheLayersThatGoToTheLaser()
    {
        var s = DocState.New();
        var cut = s.Layers[0] with { Material = new MaterialRef(Ply, "Plywood 3 mm") };
        var fill = Layer.Default(1, LayerMode.Fill) with { Material = new MaterialRef(Ply, "Plywood 3 mm") };
        var plain = Layer.Default(2, LayerMode.Score);
        var guide = Layer.Default(3, LayerMode.Cut) with { Output = false, Material = new MaterialRef(Guid.NewGuid(), "Acrylic") };
        s = s with { Layers = [cut, fill, plain, guide] };
        s = Ops.AddShapes(s, [Box(cut.Id, 0, 0, 50, 20), Box(fill.Id, 60, 30, 40, 20), Box(plain.Id, 0, 0, 10, 10), Box(guide.Id, 0, 0, 500, 500)]);

        var m = QuoteMath.Materials(s);
        Assert.Equal(2, m.Count);
        Assert.Equal("Plywood 3 mm", m[0].Label);         // both plywood layers as one box: 0,0 to 100,50
        Assert.Equal(100 * 50, m[0].AreaMm2, 6);
        Assert.Equal(QuoteMath.OtherKey, m[1].Key);        // a layer without a library material
        Assert.Equal(100, m[1].AreaMm2, 6);
        // The guide layer (not sent) isn't material.
        Assert.DoesNotContain(m, x => x.Label == "Acrylic");
    }

    [Fact]
    public void TheLastRealRunIsTheNewestFinishedRunOfTheSameDesign()
    {
        var t = new DateTime(2026, 11, 1, 12, 0, 0);
        JobRecord R(int minutes, string design, double est, double took, bool done = true) => new(t.AddMinutes(minutes), design, est, took, done, 1, 10, 10);
        var history = new[]
        {
            R(0, "Coaster", 600, 650),
            R(10, "Coaster", 610, 700),
            R(20, "Coaster", 600, 400, done: false), // stopped part-way
            R(30, "Coaster", 2000, 2100),            // the design changed since
            R(40, "Sign", 600, 900),
        };
        Assert.Equal(700, QuoteMath.LastRun(history, "coaster", 600));
        Assert.Null(QuoteMath.LastRun(history, "Coaster", 1200));
        Assert.Null(QuoteMath.LastRun(history, "Badge", 600));
    }

    [Fact]
    public void TheJobsQuoteIsKeptInTheDesign()
    {
        var job = new QuoteJob { Title = "Coasters", Customer = "Sam's Café", Runs = 4, PiecesPerRun = 6, WholeSheets = true, Time = TimeSource.LastRun, Extras = [new("Box", 2, true)] };
        var s = DocState.New();
        s = s with { AddonData = s.AddonData.SetItem(QuoteJob.Key, job.ToJson()) };
        var back = QuoteJob.From(DocFile.Deserialize(DocFile.Serialize(s)));
        Assert.Equal(job.ToJson(), back.ToJson());
        Assert.Equal(TimeSource.LastRun, back.Time);
        Assert.Equal("Box", Assert.Single(back.Extras).Name);
        // Nothing kept, or something unreadable: a new quote.
        Assert.Equal(1, QuoteJob.From(DocState.New()).Runs);
        Assert.Equal(1, QuoteJob.From(s with { AddonData = s.AddonData.SetItem(QuoteJob.Key, "{not json") }).Runs);
    }

    [Fact]
    public void RatesAreSavedAndADamagedFileGivesTheDefaults()
    {
        var dir = Directory.CreateTempSubdirectory("pc-quote-").FullName;
        try
        {
            var path = Path.Combine(dir, "rates.json");
            Assert.Equal(15, QuoteRates.Load(path).MachinePerHour); // none yet
            var r = Rates();
            r.Business = "Laser Kyle";
            r.NextNumber = 42;
            r.Save(path);
            var back = QuoteRates.Load(path);
            Assert.Equal("Laser Kyle", back.Business);
            Assert.Equal(42, back.NextNumber);
            Assert.Equal(9, back.Sheets[Ply.ToString("N")].Price);
            File.WriteAllText(path, "{ damaged");
            Assert.Equal(15, QuoteRates.Load(path).MachinePerHour);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void TheQuoteIsWrittenAsTextAndAsAPdf()
    {
        var r = Rates();
        r.Business = "Laser Kyle";
        r.Contact = "kyle@example.com\n555-0100";
        var job = new QuoteJob { Customer = "Sam's Café", Runs = 2, PiecesPerRun = 4 };
        var q = QuoteMath.Price(900, [PlyUse], job, r, "Coasters");
        var text = QuoteMath.Text(q, job, r, "Coasters", 7, new DateTime(2026, 11, 2));
        Assert.Contains("Laser Kyle", text);
        Assert.Contains("Quote Q-0007", text);
        Assert.Contains("For: Sam's Café", text);
        Assert.Contains($"Total: {QuoteMath.Format(q.Total, "$")}", text);
        Assert.Contains("a piece for 8 pieces", text);
        Assert.Contains("Prices are valid for 30 days.", text);

        var dir = Directory.CreateTempSubdirectory("pc-quote-").FullName;
        try
        {
            var picture = PhotonCanvas.Doc.Export.DesignExport.Png(Ops.AddShapes(DocState.New(), [Box(DocState.New().Layers[0].Id, 0, 0, 40, 20)]), 50);
            var paper = new QuotePaper("Coasters", 7, new DateTime(2026, 11, 2), picture);
            var path = Path.Combine(dir, "quote.pdf");
            QuotePdf.Save(path, q, job, r, paper);
            var head = new byte[5];
            using (var f = File.OpenRead(path)) f.ReadExactly(head);
            Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(head));
            Assert.True(new FileInfo(path).Length > 1000);
            Assert.True(QuotePdf.RenderPng(q, job, r, paper).Length > 1000);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ADesignsTimeComesFromTheJobBuilder()
    {
        // The window builds the job as Ship It would; its estimate is what a run costs.
        var s = DocState.New();
        s = Ops.AddShapes(s, [Box(s.Layers[0].Id, 10, 10, 30, 30)]);
        var built = JobBuilder.Build(s, new JobOptions());
        Assert.True(built.EstimateS > 0);
        var q = QuoteMath.Price(built.EstimateS, QuoteMath.Materials(s), new QuoteJob(), Rates(), "");
        Assert.Equal(built.EstimateS / 3600 * 12, q.Costs.Single(c => c.Kind == CostKind.Machine).Amount, 9);
    }
}
