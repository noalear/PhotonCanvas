using System.Collections.Immutable;
using Clipper2Lib;
using PhotonCanvas.Addon.Automata;
using PhotonCanvas.Addon.Mechanism;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using Mech = PhotonCanvas.Addon.Mechanism.Mechanism;

namespace PhotonCanvas.Tests;

/// <summary>The toys and automata maker: figures, the figure box's levers and turntables, tunes, MIDI files and music box strips.</summary>
public class AutomataTests
{
    // ---------------------------------------------------------------- figures

    public static TheoryData<string> FigureKeys()
    {
        var d = new TheoryData<string>();
        foreach (var k in Figures.Kinds) d.Add(k.Key);
        return d;
    }

    [Theory]
    [MemberData(nameof(FigureKeys))]
    public void EachFigureIsOnePieceOfTheHeightAsked(string key)
    {
        var f = Figures.Make(key, 40, false)!;
        var b = Poly.Bounds(f);
        Assert.InRange(b.Height, 39.9, 40.1);
        Assert.InRange(b.MinY, -0.01, 0.01);
        // One outline (holes allowed): a figure that came apart would drop pieces when cut.
        Assert.Single(f, p => Clipper.IsPositive(p));
        // It stands near its middle: something to stand on within a few mm of x = 0.
        Assert.True(Poly.Overlap(f, [Poly.Rect(-6, 0, 6, 3)]) > 1, $"{key} has nothing to stand on near its middle.");
        var flipped = Poly.Bounds(Figures.Make(key, 40, true)!);
        Assert.InRange(flipped.MinX, -b.MaxX - 0.01, -b.MaxX + 0.01);
    }

    // ---------------------------------------------------------------- the figure box

    public static TheoryData<int> PresetIndexes()
    {
        var d = new TheoryData<int>();
        for (int i = 0; i < FigureBoxSpec.All.Length; i++) d.Add(i);
        return d;
    }

    [Theory]
    [MemberData(nameof(PresetIndexes))]
    public void EveryReadyMadeBoxWorksAndFitsTheBed(int preset)
    {
        var spec = new FigureBoxSpec { Stations = FigureBoxSpec.All[preset].Stations };
        var m = spec.Build();
        Assert.DoesNotContain(m.Findings, f => f.Level == Severity.Bad);
        Assert.Empty(ToyPlacement.TooBig(m, MachineProfile.F2Ultra));
        Assert.All(m.Strip, s => Assert.NotEqual(Severity.Bad, s));
        Assert.Contains(m.Parts, p => p.Name == "Top");
        Assert.Contains(m.Parts, p => p.Name == "Crank");
    }

    /// <summary>Every body called <paramref name="name"/> in the front view, placed at a turn of the handle.</summary>
    static Paths64 Placed(Mech m, string name, double a)
    {
        var front = (PlanarPanel)m.Panels[0];
        var o = new Paths64();
        foreach (var b in front.Bodies.Where(b => b.Name == name)) o.AddRange(b.PlacedAt(a));
        return o;
    }

    static double TopOfBox(Mech m) => Poly.Bounds(Placed(m, "Top", 0)).MaxY;

    [Fact]
    public void FiguresStayAboveTheBoxAndNeverMeet()
    {
        var spec = new FigureBoxSpec { Stations = FigureBoxSpec.All[0].Stations };
        var m = spec.Build();
        double top = TopOfBox(m);
        var names = new[] { "Figure 1", "Lever 2", "Figure 3" };
        for (int i = 0; i < 72; i++)
        {
            double a = 2 * Math.PI * i / 72;
            var shapes = names.Select(n => Placed(m, n, a)).ToList();
            foreach (var s in shapes) Assert.True(Poly.Bounds(s).MinY >= top + 0.5, $"a figure goes into the box at {i * 5}°");
            for (int x = 0; x < shapes.Count; x++)
                for (int y = x + 1; y < shapes.Count; y++)
                    Assert.True(Poly.Overlap(shapes[x], shapes[y]) < 0.01, $"{names[x]} and {names[y]} touch at {i * 5}°");
        }
    }

    [Fact]
    public void ALeverTipsAsItsRodRises()
    {
        // Level when the rod's top is just under the pivot by the lever's half width.
        Assert.Equal(0, FigureBoxSpec.TiltFor(30, -5, 5), 9);
        // A little higher: tips up by about the rise over the distance.
        Assert.Equal(Math.Atan(2.0 / 30), FigureBoxSpec.TiltFor(30, -3, 5), 2);
        Assert.True(FigureBoxSpec.TiltFor(30, -9, 5) < 0);
        // Exactly: the rod's top point is on the lever's underside, h below the line through the pivot.
        foreach (var d in new[] { -12.0, -7, -5, 0, 6 })
        {
            double th = FigureBoxSpec.TiltFor(30, d, 5);
            double signed = -30 * Math.Sin(th) + d * Math.Cos(th);
            Assert.Equal(-5, signed, 9);
        }
    }

    [Fact]
    public void ANoddingFigureRidesOnItsRod()
    {
        var st = new FigureStation { Move = FigureMove.Nod, Figure = "cat", PivotMm = 30, ArmMm = 50, Steps = CamPresets.All[0].Steps };
        var m = new FigureBoxSpec { Stations = [st] }.Build();
        Assert.DoesNotContain(m.Findings, f => f.Level == Severity.Bad);
        for (int i = 0; i < 36; i++)
        {
            double a = 2 * Math.PI * i / 36;
            var lever = Placed(m, "Lever 1", a);
            var rod = Placed(m, "Rod 1", a);
            // Touching (the rod's top meets the lever's underside), never pushing into it.
            double rodTop = Poly.Bounds(rod).MaxY;
            var rb = Poly.Bounds(rod);
            Assert.True(Poly.Overlap(lever, [Poly.Rect(rb.MinX, rodTop - 0.4, rb.MaxX, rodTop + 0.4)]) > 0.01, $"the lever leaves its rod at {i * 10}°");
            Assert.True(Poly.Overlap(lever, [Poly.Rect(rb.MinX, rb.MinY, rb.MaxX, rodTop - 0.3)]) < 0.05, $"the rod pushes into the lever at {i * 10}°");
        }
        Assert.Contains(m.Findings, f => f.Level == Severity.Good && f.Text.Contains("nods through"));
    }

    [Fact]
    public void APeckingFigureGetsACoinToKeepItOnItsRod()
    {
        var st = new FigureStation { Move = FigureMove.Nod, Figure = "bird", Dips = true, PivotMm = 20, ArmMm = 50, FigureMm = 40 };
        var m = new FigureBoxSpec { Stations = [st] }.Build();
        Assert.Contains(m.Findings, f => f.Text.Contains("coin"));
        Assert.Contains(m.Shopping, s => s.Contains("coin"));
        Assert.NotEmpty(m.Parts.Single(p => p.Name == "Lever 1").Marks);
    }

    [Fact]
    public void ATurntableTurnsWheelOverOffsetTimesAHandleTurn()
    {
        var st = new FigureStation { Move = FigureMove.Spin, Figure = "star", WheelMm = 20, OffsetMm = 10 };
        var m = new FigureBoxSpec { Stations = [st], BandMm = 2.5 }.Build();
        Assert.Contains(m.Findings, f => f.Level == Severity.Good && f.Text.Contains("turns 2 times"));
        Assert.Contains(m.Parts, p => p.Name == "Turntable 1");
        Assert.Equal(2, m.Parts.Single(p => p.Name == "Wheel side 1").Copies);
        Assert.Contains(m.Shopping, s => s.Contains("spindle"));
        // The drive wheel's band reaches the turntable's underside; its sides don't.
        var wheel = Poly.Bounds(Placed(m, "Band", 0));
        var table = Poly.Bounds(Placed(m, "Turntable 1", 0));
        Assert.Equal(table.MinY, wheel.MaxY, 3);
        Assert.True(Poly.Bounds(Placed(m, "Drive wheel 1", 0)).MaxY < table.MinY);

        var tooClose = new FigureBoxSpec { Stations = [st with { OffsetMm = 5 }] }.Build();
        Assert.Contains(tooClose.Findings, f => f.Level == Severity.Bad && f.Text.Contains("rub its spindle"));
    }

    [Fact]
    public void ABobbingFigureSlotsOntoItsRod()
    {
        var spec = new FigureBoxSpec { Stations = [new FigureStation { Figure = "fish" }], ThicknessMm = 3, SlotFitMm = 0.1 };
        var m = spec.Build();
        var fig = m.Parts.Single(p => p.Name == "Figure 1").Shape;
        var rod = m.Parts.Single(p => p.Name == "Rod 1").Shape;
        // Each has a slot the other's thickness wide, half the joint deep: from below in the figure, from above in the rod.
        Assert.False(Poly.Inside(fig, new Vec2(0, 1)));
        Assert.False(Poly.Inside(fig, new Vec2(1.5, FigureBoxSpec.JointMm / 2 - 0.5)));
        Assert.True(Poly.Inside(fig, new Vec2(1.7, 1)));
        Assert.True(Poly.Inside(fig, new Vec2(0, FigureBoxSpec.JointMm / 2 + 0.5)));
        double top = Poly.Bounds(rod).MaxY;
        Assert.False(Poly.Inside(rod, new Vec2(0, top - 1)));
        Assert.True(Poly.Inside(rod, new Vec2(0, top - FigureBoxSpec.JointMm / 2 - 0.5)));
    }

    [Fact]
    public void SettingsSurviveBeingSavedAndAreStillAPreset()
    {
        var spec = new FigureBoxSpec { Stations = FigureBoxSpec.All[2].Stations, CrankMm = 31 };
        var back = Assert.IsType<FigureBoxSpec>(ToySpec.FromJson(spec.ToJson()));
        Assert.Equal(spec.ToJson(), back.ToJson());
        Assert.Same(FigureBoxSpec.All[2], FigureBoxSpec.Find(back.Stations));
        var strip = new StripSpec { Transpose = -3, Tune = "C5 D5:2 E5+G5" };
        Assert.Equal(strip.ToJson(), Assert.IsType<StripSpec>(ToySpec.FromJson(strip.ToJson())).ToJson());
        Assert.Null(ToySpec.FromJson("{\"kind\":\"gears\"}"));
    }

    [Fact]
    public void ToysGoOnTheirOwnLayersAndCanBeOpenedAgain()
    {
        var spec = new FigureBoxSpec { Stations = FigureBoxSpec.All[4].Stations };
        var (s, sheets, id) = Placement.Place(DocState.New(), spec.Build(), spec.ToJson(), MachineProfile.F2Ultra, new PlaceOptions(), ToyPlacement.Target);
        Assert.True(sheets >= 1);
        Assert.Contains(s.Layers, l => l.Name == "Toy · cut");
        var mine = Placement.ShapesOf(s, id, ToyPlacement.Target);
        Assert.NotEmpty(mine);
        var found = ToyPlacement.Find(s, [mine.First()]);
        Assert.NotNull(found);
        Assert.Equal(id, found.Value.Id);
        Assert.Equal(spec.ToJson(), found.Value.Spec.ToJson());
        // The mechanism maker leaves them alone.
        Assert.Null(Placement.Find(s, mine));
        Assert.Empty(Placement.ShapesOf(s, id));
        // Made again: the old parts go.
        var strip = new StripSpec();
        var (again, _, id2) = Placement.Place(s, strip.Build(), strip.ToJson(), MachineProfile.F2Ultra, new PlaceOptions { Replace = id }, ToyPlacement.Target);
        Assert.Equal(id, id2);
        Assert.IsType<StripSpec>(ToyPlacement.Find(again, Placement.ShapesOf(again, id, ToyPlacement.Target))!.Value.Spec);
    }

    [Fact]
    public void TheCrankIsOnePiece()
    {
        // A link's bar winds the same way as its round ends, so they join (they used to cancel where they overlapped).
        var link = Shapes.Link(default, new Vec2(28, 0), 16);
        Assert.Single(link);
        Assert.Equal(28 * 16 + Math.PI * 64, Poly.Area(link), 0);
        foreach (var crank in new[] { new FigureBoxSpec().Build().Parts.Single(p => p.Name == "Crank"), new AutomatonSpec().Build().Parts.Single(p => p.Name == "Crank") })
            Assert.Single(crank.Shape, p => Clipper.IsPositive(p));
    }

    // ---------------------------------------------------------------- tunes

    [Fact]
    public void TunesAreReadFromText()
    {
        var r = Melody.Parse("C4 D4:2 | - E4+G4:0.5 r:1/2 Bb3 c#5 X9 C4:x");
        Assert.Equal(2, r.Problems.Count);
        Assert.Equal(
            [new(0, 60), new(1, 62), new(4, 64), new(4, 67), new(5, 58), new(6, 73)],
            r.Notes);
        Assert.Equal(8, r.Beats);
        Assert.Equal(69, Notes.Parse("A4"));
        Assert.Equal("F#5", Notes.Name(78));
        Assert.Null(Notes.Parse("H4"));
    }

    [Fact]
    public void TunesWrittenOutReadBackTheSame()
    {
        var notes = new List<NoteEvent> { new(0, 60), new(0, 64), new(1, 62), new(2.5, 67), new(4, 72), new(7.25, 48) };
        var back = Melody.Parse(Melody.Write(notes));
        Assert.Empty(back.Problems);
        Assert.Equal(notes.OrderBy(n => n.Beat).ThenBy(n => n.Pitch), back.Notes.OrderBy(n => n.Beat).ThenBy(n => n.Pitch));
        foreach (var (_, text) in Melody.Tunes) Assert.Empty(Melody.Parse(text).Problems);
    }

    /// <summary>A small MIDI file: format 1, 480 ticks a beat, a tempo track and a track of notes (running status, a note-on at 0 velocity as note-off, a drum).</summary>
    static byte[] SmallMidi()
    {
        var b = new List<byte>();
        void S(string s) => b.AddRange(System.Text.Encoding.ASCII.GetBytes(s));
        void U32(int v) => b.AddRange([(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v]);
        void U16(int v) => b.AddRange([(byte)(v >> 8), (byte)v]);
        S("MThd"); U32(6); U16(1); U16(2); U16(480);
        byte[] tempo = [0x00, 0xFF, 0x51, 0x03, 0x07, 0xA1, 0x20, 0x00, 0xFF, 0x2F, 0x00];
        S("MTrk"); U32(tempo.Length); b.AddRange(tempo);
        byte[] notes =
        [
            0x00, 0x90, 60, 100,        // C4 at 0
            0x00, 64, 90,               // E4 at 0 (running status)
            0x83, 0x60, 60, 0,          // 480 ticks later: C4 off (velocity 0)
            0x00, 0x80, 64, 0,          // E4 off
            0x00, 0x99, 36, 100,        // a drum: left out
            0x81, 0x70, 0x90, 67, 80,   // 240 ticks later (beat 1.5): G4
            0x00, 0xC0, 5,              // a programme change
            0x83, 0x60, 0x80, 67, 0,
            0x00, 0xFF, 0x2F, 0x00,
        ];
        S("MTrk"); U32(notes.Length); b.AddRange(notes);
        return [.. b];
    }

    [Fact]
    public void MidiFilesAreReadInBeats()
    {
        var notes = MidiFile.Read(SmallMidi());
        Assert.Equal([new(0, 60), new(0, 64), new(1.5, 67)], notes);
        Assert.Throws<InvalidDataException>(() => MidiFile.Read([1, 2, 3, 4, 5]));
    }

    // ---------------------------------------------------------------- strips

    static int Holes(Paths64 p) => p.Count(x => !Clipper.IsPositive(x));

    [Fact]
    public void EachNoteIsAHoleInItsLaneABeatApart()
    {
        var spec = new StripSpec { Tune = "C4 - G4+C5", LeadMm = 30, BeatMm = 8 };
        var m = spec.Build();
        var strip = m.Parts.Single().Shape;
        Assert.Equal(3, Holes(strip));
        // C4 is the 30-note box's sixth note: 6 + 5 × 2 mm from the edge.
        Assert.False(Poly.Inside(strip, new Vec2(30, 16)));
        Assert.True(Poly.Inside(strip, new Vec2(30, 18)));
        Assert.False(Poly.Inside(strip, new Vec2(46, 6 + 10 * 2)));
        Assert.False(Poly.Inside(strip, new Vec2(46, 6 + 15 * 2)));
        Assert.Equal(70, Poly.Bounds(strip).Height, 3);
        Assert.Contains(m.Findings, f => f.Level == Severity.Good && f.Text.StartsWith("Every note"));
        // Turned over, the lanes come from the other edge.
        var mirrored = (spec with { Mirror = true }).Build().Parts.Single().Shape;
        Assert.False(Poly.Inside(mirrored, new Vec2(30, 70 - 16)));
    }

    [Fact]
    public void NotesTheBoxHasNotGotAreMovedOrLeftOut()
    {
        // C#4 isn't on the 30-note box; C#5 is.
        var moved = new StripSpec { Tune = "C#4 D4" }.Build();
        Assert.Contains(moved.Findings, f => f.Level == Severity.Note && f.Text.Contains("octave"));
        var left = new StripSpec { Tune = "C#4 D4", Octaves = false }.Build();
        Assert.Contains(left.Findings, f => f.Level == Severity.Warn && f.Text.Contains("C#4"));
        Assert.Equal(1, Holes(left.Parts.Single().Shape));
    }

    [Fact]
    public void ATuneIsMovedToTheKeyThatFitsTheBox()
    {
        var fifteen = Movement.Find("15")!;
        var notes = Melody.Parse("C#5 D#5 F5 F#5 G#5").Notes;
        Assert.Equal(-1, StripSpec.BestTranspose(notes, fifteen.Pitches, false));
        Assert.Equal(0, StripSpec.BestTranspose(Melody.Parse("C5 E5 G5").Notes, fifteen.Pitches, false));
    }

    [Fact]
    public void NotesRepeatedTooCloseAreFound()
    {
        var torn = new StripSpec { Tune = "C5:0.25 C5:0.25 C5", BeatMm = 8 }.Build();
        Assert.Contains(torn.Findings, f => f.Level == Severity.Bad && f.Text.Contains("tears"));
        var quick = new StripSpec { Tune = "C5:0.5 C5", BeatMm = 8, HoleMm = 2.4 }.Build();
        Assert.Contains(quick.Findings, f => f.Level == Severity.Warn && f.Text.Contains("sprung back"));
        var fine = new StripSpec { Tune = "C5 C5", BeatMm = 8 }.Build();
        Assert.DoesNotContain(fine.Findings, f => f.Level >= Severity.Warn);
    }

    [Fact]
    public void ALongStripIsCutIntoPiecesThatFitTheBedAndLockTogether()
    {
        var spec = new StripSpec { Tune = Melody.Tunes[0].Text, BeatMm = 10, MaxPieceMm = 200 };
        var m = spec.Build();
        Assert.True(m.Parts.Count >= 2);
        Assert.All(m.Parts, p => Assert.True(Poly.Bounds(p.Shape).Width <= 200 + 1e-6, $"{p.Name} is {Poly.Bounds(p.Shape).Width:0.#} mm"));
        Assert.Empty(ToyPlacement.TooBig(m, MachineProfile.F2Ultra));
        // Every note is still there, in one piece or another.
        var (holes, _, _, _) = spec.Punches();
        Assert.Equal(holes.Count, m.Parts.Sum(p => Holes(p.Shape)));
        // Each dovetail fills the next piece's notch: laid together, the pieces cover the strip with no overlap.
        var all = m.Parts.Select(p => p.Shape).ToList();
        for (int i = 0; i + 1 < all.Count; i++)
            Assert.True(Poly.Overlap(all[i], all[i + 1]) < 0.01);
        double sum = all.Sum(Poly.Area);
        double whole = Poly.Area(Poly.Union(new Paths64(all.SelectMany(p => p))));
        Assert.Equal(whole, sum, 0);
        Assert.Contains(m.Findings, f => f.Text.Contains("pieces"));
    }

    [Fact]
    public void TheTestStripPlaysEveryNoteOnce()
    {
        foreach (var box in Movement.All)
        {
            var spec = new StripSpec().WithMovement(box) with { Tune = Melody.EveryNote(box.Pitches) };
            var m = spec.Build();
            Assert.Equal(box.Pitches.Length, m.Parts.Sum(p => Holes(p.Shape)));
            Assert.DoesNotContain(m.Findings, f => f.Level >= Severity.Warn);
            // The lanes fit across the strip.
            Assert.True(box.FirstMm + (box.Pitches.Length - 1) * box.PitchMm + box.HoleMm / 2 < box.WidthMm - 1);
        }
    }
}
