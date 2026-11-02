using PhotonCanvas.Addon.RecipeTuner;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Materials;
using Xunit;
using Xunit.Abstractions;

namespace PhotonCanvas.Tests;

public class RecipeTunerTests(ITestOutputHelper output)
{
    static readonly MachineProfile M = MachineProfile.F2Ultra;

    static Material Basswood() => new() { Id = new Guid("7b0c6c55-0d0e-4c49-9a43-3f2d5e1b9a01"), Name = "Basswood", Category = "Wood", ThicknessMm = 3 };

    static ProcessSettings Cut(double power, double speed, int passes) =>
        new() { Mode = LayerMode.Cut, Laser = LaserSettings.Blue(power, speed) with { Passes = passes } };

    /// <summary>A made-up material that cuts best at 65% · 30 mm/s · 6 passes, and worse the further away.</summary>
    static int Truth(ProcessSettings s)
    {
        double p = (s.Laser.Power - 65) / 20, v = Math.Log(s.Laser.Speed / 30) / Math.Log(1.8), n = Math.Log(s.Laser.Passes / 6.0) / Math.Log(2);
        return (int)Math.Clamp(Math.Round(5 * Math.Exp(-(p * p + v * v + n * n))), 1, 5);
    }

    static Rated Own(ProcessSettings s, int rating, int heat = 0) => new(s, rating, 1, "Basswood 3 mm", true, DateTime.Now, heat);

    [Fact]
    public void HeatFeedbackFindsSettingsFarFromTheStart()
    {
        // Best at 90% · 12 mm/s · 3 passes; the start (70% · 40 mm/s · 8 passes) is 1 star and too little heat.
        static int Far(ProcessSettings s)
        {
            double p = (s.Laser.Power - 90) / 20, v = Math.Log(s.Laser.Speed / 12) / Math.Log(1.8), n = Math.Log(s.Laser.Passes / 3.0) / Math.Log(2);
            return (int)Math.Clamp(Math.Round(5 * Math.Exp(-(p * p + v * v + n * n))), 1, 5);
        }
        static int Heat(ProcessSettings s)
        {
            double e = s.Laser.Power * s.Laser.Passes / s.Laser.Speed, best = 90 * 3 / 12.0;
            return Far(s) == 5 ? 0 : e < best / 1.25 ? -1 : e > best * 1.25 ? 1 : 0;
        }
        var start = Cut(70, 40, 8);
        var ev = new List<Rated> { Own(start, Far(start), Heat(start)) };
        var ask = new TuneAsk(Basswood(), RecipePurpose.Cut, LightSource.Blue, [Knob.Power, Knob.Speed, Knob.Passes], 5);
        int rounds = 0;
        for (; rounds < 6 && ev.Max(e => e.Rating) < 5; rounds++)
            foreach (var s in Tuner.Suggest(ask, ev, M).Suggestions)
                ev.Add(Own(s.Settings, Far(s.Settings), Heat(s.Settings)));
        output.WriteLine($"{rounds} rounds, best {ev.Max(e => e.Rating)}");
        Assert.Equal(5, ev.Max(e => e.Rating));
    }

    [Fact]
    public void RatingRoundsFindTheBestSettings()
    {
        var mat = Basswood();
        var ev = new List<Rated> { Own(Cut(70, 40, 8), Truth(Cut(70, 40, 8))), Own(Cut(40, 60, 2), Truth(Cut(40, 60, 2))) };
        var ask = new TuneAsk(mat, RecipePurpose.Cut, LightSource.Blue, [Knob.Power, Knob.Speed, Knob.Passes], 5);
        int firstBest = ev.Max(e => e.Rating);
        for (int round = 0; round < 5; round++)
        {
            var r = Tuner.Suggest(ask, ev, M);
            Assert.Equal(5, r.Suggestions.Count);
            Assert.Equal(SuggestionKind.BestGuess, r.Suggestions[0].Kind);
            foreach (var s in r.Suggestions)
            {
                Assert.InRange(s.Settings.Laser.Power, 1, 100);
                Assert.InRange(s.Settings.Laser.Speed, M.MinSpeed, M.MaxSpeed);
                Assert.InRange(s.Settings.Laser.Passes, 1, 100);
                Assert.DoesNotContain(ev, e => e.Settings.Laser == s.Settings.Laser);
                Assert.False(string.IsNullOrWhiteSpace(s.Why));
                ev.Add(Own(s.Settings, Truth(s.Settings)));
                output.WriteLine($"round {round}: {Knobs.Summary(s.Settings, RecipePurpose.Cut)} expected {s.Expected:0.0}±{s.Uncertainty:0.0} got {Truth(s.Settings)}  ({s.Kind}: {s.Why})");
            }
            Assert.Equal(5, r.Suggestions.Select(s => s.Settings.Laser).Distinct().Count());
        }
        Assert.True(ev.Max(e => e.Rating) > firstBest, "the rounds found nothing better than the start");
        Assert.Equal(5, ev.Max(e => e.Rating));
    }

    [Fact]
    public void NothingRatedGivesASpreadAroundTheStart()
    {
        var mat = Basswood() with { Recipes = [new Recipe { Purpose = RecipePurpose.Cut, Title = "From xTool", Settings = Cut(80, 20, 4), TestedBy = "xTool", Tested = true }] };
        var r = Tuner.Suggest(new TuneAsk(mat, RecipePurpose.Cut, LightSource.Blue, [Knob.Power, Knob.Speed], 6), [], M);
        Assert.Equal(6, r.Suggestions.Count);
        Assert.All(r.Suggestions, s => Assert.Equal(SuggestionKind.Spread, s.Kind));
        Assert.Equal(Cut(80, 20, 4).Laser, r.Suggestions[0].Settings.Laser);
        Assert.Contains("From xTool", r.StartFrom);
        Assert.Equal(6, r.Suggestions.Select(s => s.Settings.Laser).Distinct().Count());
        foreach (var s in r.Suggestions)
        {
            // Within about a step of the start, and only power and speed changed.
            Assert.InRange(s.Settings.Laser.Power, 80 - 25, 100);
            Assert.InRange(s.Settings.Laser.Speed, 20 / 1.9, 20 * 1.9);
            Assert.Equal(4, s.Settings.Laser.Passes);
        }
    }

    [Fact]
    public void OnlyTheTickedSettingsChange()
    {
        var mat = Basswood();
        var ev = new List<Rated> { Own(Cut(70, 40, 8), 4), Own(Cut(60, 40, 8), 3) };
        var r = Tuner.Suggest(new TuneAsk(mat, RecipePurpose.Cut, LightSource.Blue, [Knob.Speed], 4), ev, M);
        Assert.NotEmpty(r.Suggestions);
        Assert.All(r.Suggestions, s => { Assert.Equal(70, s.Settings.Laser.Power); Assert.Equal(8, s.Settings.Laser.Passes); });
    }

    [Fact]
    public void TheSameQuestionGetsTheSameAnswer()
    {
        var mat = Basswood();
        var ev = new List<Rated> { Own(Cut(70, 40, 8), 4), Own(Cut(60, 30, 6), 5), Own(Cut(90, 60, 2), 1) };
        var ask = new TuneAsk(mat, RecipePurpose.Cut, LightSource.Blue, [Knob.Power, Knob.Speed, Knob.Passes], 6);
        var a = Tuner.Suggest(ask, ev, M).Suggestions.Select(s => s.Settings).ToList();
        var b = Tuner.Suggest(ask, ev, M).Suggestions.Select(s => s.Settings).ToList();
        Assert.Equal(a, b);
    }

    [Fact]
    public void OtherMaterialsCountForLess()
    {
        var lib = new MaterialLibrary(Path.Combine(Path.GetTempPath(), "pc-tuner-" + Guid.NewGuid().ToString("N")));
        var mine = Basswood() with { Recipes = [new Recipe { Purpose = RecipePurpose.Cut, Settings = Cut(70, 40, 8), Rating = 2 }] };
        var ply = new Material { Name = "Plywood", Category = "Wood", ThicknessMm = 3, Recipes = [new Recipe { Purpose = RecipePurpose.Cut, Settings = Cut(70, 40, 8), Rating = 5 }] };
        var thick = new Material { Name = "Oak", Category = "Wood", ThicknessMm = 12, Recipes = [new Recipe { Purpose = RecipePurpose.Cut, Settings = Cut(90, 10, 10), Rating = 5 }] };
        var acrylic = new Material { Name = "Acrylic", Category = "Acrylic", ThicknessMm = 3, Recipes = [new Recipe { Purpose = RecipePurpose.Cut, Settings = Cut(70, 40, 8), Rating = 5 }] };
        var engrave = new Material { Name = "Plywood", Category = "Wood", ThicknessMm = 3, Recipes = [new Recipe { Purpose = RecipePurpose.Engrave, Settings = Cut(30, 400, 1), Rating = 5 }] };
        var unrated = new Material { Name = "Pine", Category = "Wood", ThicknessMm = 3, Recipes = [new Recipe { Purpose = RecipePurpose.Cut, Settings = Cut(70, 40, 8) }] };
        foreach (var m in new[] { mine, ply, thick, acrylic, engrave, unrated }) lib.Upsert(m);

        var ev = Tuner.Gather(lib, [], mine, RecipePurpose.Cut, LightSource.Blue, M.DeviceCode);
        Assert.Equal(3, ev.Count); // not the acrylic, the engraving or the unrated one
        Assert.Equal(1, ev.Single(e => e.Own).Trust);
        double plyTrust = ev.Single(e => e.From.StartsWith("Plywood")).Trust, oakTrust = ev.Single(e => e.From.StartsWith("Oak")).Trust;
        Assert.InRange(plyTrust, 0.3, 0.7);
        Assert.True(oakTrust < plyTrust);

        // Same settings: your own 2 stars outweighs the plywood's 5.
        var r = Tuner.Suggest(new TuneAsk(mine, RecipePurpose.Cut, LightSource.Blue, [Knob.Power, Knob.Speed, Knob.Passes], 3), ev, M);
        Assert.Contains("your best so far", r.StartFrom);
        Assert.Contains(ev, e => e.Own && e.Rating == 2);
    }

    [Fact]
    public void KeptTriesCountUnlessTheyAreInTheLibrary()
    {
        var lib = new MaterialLibrary(Path.Combine(Path.GetTempPath(), "pc-tuner-" + Guid.NewGuid().ToString("N")));
        var recipe = new Recipe { Purpose = RecipePurpose.Cut, Settings = Cut(70, 40, 8), Rating = 4 };
        var mine = Basswood() with { Recipes = [recipe] };
        lib.Upsert(mine);
        var round = Guid.NewGuid();
        var kept = new[]
        {
            new TriedRecord(round, 1, mine.Id, "Basswood", "Wood", 3, RecipePurpose.Cut, Cut(70, 40, 8), 4, DateTime.Now, M.DeviceCode, recipe.Id),
            new TriedRecord(round, 2, mine.Id, "Basswood", "Wood", 3, RecipePurpose.Cut, Cut(100, 10, 10), 1, DateTime.Now, M.DeviceCode, null),
        };
        var ev = Tuner.Gather(lib, kept, mine, RecipePurpose.Cut, LightSource.Blue, M.DeviceCode);
        Assert.Equal(2, ev.Count);
        Assert.Contains(ev, e => e.Rating == 1 && e.Own);
    }

    [Fact]
    public void FiberSettingsStayInsideTheSource()
    {
        var mat = new Material { Name = "Stainless", Category = "Metal", ThicknessMm = 1 };
        var mark = new ProcessSettings { Mode = LayerMode.Fill, LinesPerCm = 300, Laser = LaserSettings.Red(50, 1000, 200, 65) };
        var ev = new List<Rated> { new(mark, 3, 1, "Stainless 1 mm", true, DateTime.Now) };
        var r = Tuner.Suggest(new TuneAsk(mat, RecipePurpose.Mark, LightSource.Red, [Knob.Power, Knob.Frequency, Knob.PulseWidth], 6), ev, M);
        Assert.NotEmpty(r.Suggestions);
        foreach (var s in r.Suggestions)
        {
            Assert.Equal(LightSource.Red, s.Settings.Laser.Light);
            var lim = M.MopaLimitFor(s.Settings.Laser.PulseWidthNs);
            Assert.NotNull(lim);
            Assert.InRange(s.Settings.Laser.FrequencyKhz, 1, lim!.Value.MaxKhz);
        }
    }

    [Fact]
    public void TriesGoInTheDesignAndComeOutAgain()
    {
        var doc = new DocState([Layer.Default(0)], []);
        var mat = Basswood();
        var tries = new[] { Cut(60, 30, 6), Cut(70, 35, 6), Cut(65, 25, 5) };
        var (s, added, round) = TuneTries.Add(doc, mat, RecipePurpose.Cut, LightSource.Blue, tries, 10, "Arial", M);
        Assert.Equal(6, added.Count); // three squares and three numbers
        var found = TuneTries.Read(s);
        Assert.Equal([1, 2, 3], found.Select(t => t.Number));
        Assert.Equal(tries.Select(t => t.Laser), found.Select(t => t.Layer.Laser));
        Assert.All(found, t => Assert.StartsWith("Test tune ", t.Layer.Name));
        Assert.All(found, t => Assert.Equal(mat.Id, t.Layer.Material?.MaterialId));
        Assert.Equal(round.Id, TuneTries.Round(s)?.Id);
        // The numbers come before the tries (cut squares drop out).
        int numbers = s.Layers.FindIndex(l => l.Name == "Test labels");
        Assert.True(numbers < s.Layers.FindIndex(l => l.Name.StartsWith("Test tune 1")));
        Assert.All(found, t => Assert.True(M.Contains(t.Bounds.MaxX, t.Bounds.MaxY)));

        // A new round replaces the old one; taking them out leaves the design as it was.
        var (s2, _, round2) = TuneTries.Add(s, mat, RecipePurpose.Cut, LightSource.Blue, tries[..2], 10, "Arial", M);
        Assert.Equal(2, TuneTries.Read(s2).Count);
        Assert.NotEqual(round.Id, round2.Id);
        var back = TuneTries.Remove(s2);
        Assert.Empty(back.Shapes);
        Assert.Single(back.Layers);
        Assert.Null(TuneTries.Round(back));
    }

    [Fact]
    public void TriesKeepClearOfTheDesign()
    {
        var layer = Layer.Default(0);
        var big = new PathShape { LayerId = layer.Id, Geometry = PathGeometry.Rect(5, 5, 100, 100) };
        var doc = new DocState([layer], [big]);
        var (s, _, _) = TuneTries.Add(doc, Basswood(), RecipePurpose.Cut, LightSource.Blue, [Cut(60, 30, 6), Cut(70, 35, 6)], 10, "Arial", M);
        Assert.All(TuneTries.Read(s), t => Assert.False(t.Bounds.Intersects(big.Bounds)));
    }

    [Fact]
    public void KeptTriesSurviveSaving()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pc-tuner-" + Guid.NewGuid().ToString("N"));
        var store = new TriesStore(dir);
        var round = Guid.NewGuid();
        store.Put(new TriedRecord(round, 1, Guid.NewGuid(), "Basswood", "Wood", 3, RecipePurpose.Cut, Cut(60, 30, 6), 2, DateTime.Now, M.DeviceCode, null));
        store.Put(new TriedRecord(round, 1, Guid.NewGuid(), "Basswood", "Wood", 3, RecipePurpose.Cut, Cut(60, 30, 6), 4, DateTime.Now, M.DeviceCode, null));
        store.Save();
        var back = TriesStore.Load(dir);
        Assert.Single(back.Tries);
        Assert.Equal(4, back.Tries[0].Rating);
        Assert.Equal(Cut(60, 30, 6).Laser, back.Tries[0].Settings.Laser);
        File.WriteAllText(back.File, "{ not json");
        Assert.Empty(TriesStore.Load(dir).Tries);
        Directory.Delete(dir, true);
    }
}
