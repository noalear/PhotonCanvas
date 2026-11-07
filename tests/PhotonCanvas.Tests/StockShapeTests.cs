using PhotonCanvas.Core;

namespace PhotonCanvas.Tests;

public class StockShapeTests
{
    /// <summary>A scan of <paramref name="shape"/> held as given, every 15° over a whole turn, at three places.</summary>
    static RotaryScan ScanOf(StockShape shape, double axis, Vec2 centre, double turn, double noise = 0)
    {
        var rnd = new Random(7);
        var angles = Enumerable.Range(0, 24).Select(i => i * 15.0).ToArray();
        return new RotaryScan
        {
            AnglesDeg = angles,
            Xs = [100, 110, 120],
            Heights = [.. angles.Select(a => new double?[] { 0, 0, 0 }.Select(_ =>
                shape.RadiusAt(a, turn, centre) is { } r ? axis + r + noise * (rnd.NextDouble() - 0.5) : (double?)null).ToArray())],
        };
    }

    [Fact]
    public void RaysMeetTheOutline()
    {
        var sq = new StockShape(StockKind.Square, 50.8);
        Assert.Equal(25.4, sq.RadiusAt(0, 0, new Vec2(0, 0))!.Value, 6);
        Assert.Equal(25.4 * Math.Sqrt(2), sq.RadiusAt(45, 0, new Vec2(0, 0))!.Value, 6);
        Assert.Equal(25.4 * Math.Sqrt(2), sq.RadiusAt(0, 45, new Vec2(0, 0))!.Value, 6); // turned onto a corner
        Assert.Equal(27.4, sq.RadiusAt(0, 0, new Vec2(0, 2))!.Value, 6);
        var rod = new StockShape(StockKind.Round, 20);
        Assert.Equal(12, rod.RadiusAt(0, 0, new Vec2(0, 2))!.Value, 6);
        Assert.Equal(8, rod.RadiusAt(180, 0, new Vec2(0, 2))!.Value, 6);
        var hex = new StockShape(StockKind.Hexagon, 20);
        Assert.Equal(10, hex.RadiusAt(0, 0, new Vec2(0, 0))!.Value, 6); // a flat on top
        Assert.Equal("2\" × 2\" square bar", sq.Describe(inches: true));
    }

    [Theory]
    [InlineData(StockKind.Square, 50.8, 0, 17, 1.5, -0.8)]
    [InlineData(StockKind.Rectangle, 40, 20, 123, 0.5, 1)]
    [InlineData(StockKind.Hexagon, 25, 0, 41, 0, 0)]
    public void AFitFindsHowTheStockSitsAndTheAxis(StockKind kind, double w, double h, double turn, double ex, double ey)
    {
        var shape = new StockShape(kind, w, h);
        var scan = ScanOf(shape, 30, new Vec2(ex, ey), turn, noise: 0.05);
        var fit = StockFitter.Fit(scan, shape, null)!;
        Assert.Equal(30, fit.AxisHeightMm, 1);
        Assert.Equal(ex, fit.Centre.X, 1);
        Assert.Equal(ey, fit.Centre.Y, 1);
        double period = shape.PeriodDeg, d = ((fit.TurnDeg - turn) % period + period * 1.5) % period - period / 2;
        Assert.InRange(Math.Abs(d), 0, 1);
        Assert.True(fit.RmsMm < 0.1, $"rms {fit.RmsMm}");
    }

    [Fact]
    public void WithTheAxisKnownItStillFindsTheTurnAndTheIdealStockReadsLikeAScan()
    {
        var shape = new StockShape(StockKind.Square, 50.8);
        var scan = ScanOf(shape, 12, new Vec2(0.3, 0), 30);
        var fit = StockFitter.Fit(scan, shape, 12)!;
        Assert.Equal(12, fit.AxisHeightMm);
        Assert.Equal(30, fit.TurnDeg, 0);
        // Turning by this brings a flat to the top.
        Assert.Equal(25.4, shape.RadiusAt(fit.FlatUpDeg(), fit.TurnDeg, new Vec2(0, 0))!.Value, 1);

        var ideal = fit.ToScan(scan.Xs, 110, 5);
        Assert.Equal("standard", ideal.Method);
        Assert.Equal("50.8 mm × 50.8 mm square bar", ideal.Stock);
        Assert.Equal(72, ideal.AnglesDeg.Length);
        Assert.True(ideal.WholeTurn);
        Assert.Equal(ideal.Total, ideal.Read);
        var r = ideal.Radii(12);
        Assert.Equal(25.4 * Math.Sqrt(2), r.SelectMany(x => x).Max()!.Value, 0);
        Assert.NotNull(ideal.ToStl(12));
        Assert.Equal("50.8 mm × 50.8 mm square bar", RotaryScan.FromJson(ideal.ToJson())!.Stock);
    }

    [Fact]
    public void TooFewReadingsGiveNoFit()
    {
        var shape = new StockShape(StockKind.Square, 50.8);
        var scan = new RotaryScan { AnglesDeg = [0, 90], Xs = [110], Heights = [[50], [50]] };
        Assert.Null(StockFitter.Fit(scan, shape, null));
        Assert.Null(StockFitter.Fit(scan, new StockShape(StockKind.Scanned, 0), 10));
    }
}
