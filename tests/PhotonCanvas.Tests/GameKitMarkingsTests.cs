using Clipper2Lib;
using PhotonCanvas.Addon.GameKit;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Tests;

/// <summary>The game kit's markings: fonts, numbers, titles, borders, ornaments and uploaded designs on gauges, rulers and templates.</summary>
public class GameKitMarkingsTests
{
    static readonly AccessorySet Sheet = new() { Items = [], ThicknessMm = 3, FitMm = 0.1 };

    static ItemKit Make(AccessoryItem it) => GameKitMaker.Make(it, Sheet, 0);

    static bool HaveFonts => Labels.Text("12", "Arial", 0, 0, 10, 6).Count > 0;

    static AccessoryItem Gauge(Markings mk, int segments = 4) => Defaults.For(AccessoryKind.MoveGauge) with { Segments = segments, Marks = mk };

    static IEnumerable<AccessoryItem> AllMarked(Markings mk) =>
    [
        Gauge(mk),
        Defaults.For(AccessoryKind.Ruler) with { Marks = mk },
        Defaults.For(AccessoryKind.Template) with { LengthUnits = 2, Shape = TemplateShape.Circle, Marks = mk },
        Defaults.For(AccessoryKind.Template) with { LengthUnits = 4, Shape = TemplateShape.Cone, Marks = mk },
        Defaults.For(AccessoryKind.Template) with { LengthUnits = 2, Shape = TemplateShape.Square, Marks = mk },
    ];

    public static IEnumerable<object[]> Designs() => MarkDesigns.Names.Select(n => new object[] { n });

    [Theory]
    [MemberData(nameof(Designs))]
    public void Every_design_marks_every_gauge_ruler_and_template_on_the_piece(string design)
    {
        var mk = MarkDesigns.Apply(new Markings(), design) with { Title = "CHARGE" };
        foreach (var it in AllMarked(mk))
        {
            var k = Make(it);
            Assert.True(k.Ok, $"{design} {it.Kind} {it.Shape}: {string.Join("; ", k.Findings.Select(f => f.Text))}");
            foreach (var p in k.Parts)
            {
                // Engraving and lines stay on the piece.
                Assert.Equal(0, Geo.Area(Geo.Subtract(p.Etched, p.Cut)), 2);
                Assert.DoesNotContain(Geo.ClipLines(p.Lines, Geo.Subtract([Geo.Rect(-1e4, -1e4, 1e4, 1e4)], Geo.Offset(p.Cut, 0.01))), l => l.Count > 1 && Spread(l) > 0.05);
            }
            var marked = k.Parts.Where(p => p.Name.StartsWith("Segment") || p.Name.Contains("template") || p.Name == "Ruler").ToList();
            Assert.NotEmpty(marked);
            if (mk.Border != BorderStyle.None || mk.Ornament.Length > 0)
                Assert.All(marked, p => Assert.True(p.Etched.Count > 0 || p.Lines.Count > 2, $"{design}: nothing on {p.Name}"));
        }
    }

    static double Spread(PathD l) { var b = Clipper.GetBounds(new PathsD { l }); return b.right - b.left + b.bottom - b.top; }

    [Fact]
    public void Every_ornament_is_drawn_and_fits_its_box()
    {
        foreach (var name in Ornaments.Names)
        {
            var s = Ornaments.Shape(name);
            Assert.True(s.Count > 0, name);
            var b = Geo.Bounds(s);
            Assert.InRange(b.left, -1.0001, 1); Assert.InRange(b.right, -1, 1.0001);
            Assert.InRange(b.top, -1.0001, 1); Assert.InRange(b.bottom, -1, 1.0001);
            Assert.True(Math.Max(b.right - b.left, b.bottom - b.top) > 1.99, name);
            // Enough ink to see, not a solid block.
            double a = Geo.Area(s);
            Assert.InRange(a, 0.25, 3.6);
        }
    }

    public static IEnumerable<object[]> Styles() => Borders.All.Where(b => b.Style is not (BorderStyle.None or BorderStyle.Art)).Select(b => new object[] { b.Style });

    [Theory]
    [MemberData(nameof(Styles))]
    public void Borders_follow_straight_edges_and_curves_and_stay_in_their_band(BorderStyle style)
    {
        foreach (var region in new PathsD[] { [Geo.Rect(0, 0, 60, 12)], [Geo.Circle(20, 20, 20, 0.01)], [[new(0, 0), new(60, -30), new(60, 30)]] })
        {
            double inset = 0.8, band = 2;
            var (a, l) = Borders.Make(region, inset, band, style, null, 3);
            Assert.True(a.Count > 0 || l.Count > 0, $"{style}: nothing drawn");
            // Inside the region, and not further in than the band.
            Assert.Equal(0, Geo.Area(Geo.Subtract(a, region)), 2);
            var middle = Geo.Offset(region, -(inset + band + 0.3));
            Assert.True(Geo.Area(Geo.Intersect(a, middle)) < 0.05, $"{style} spills inward");
            Assert.DoesNotContain(Geo.ClipLines(l, middle), x => Spread(x) > 0.1);
        }
    }

    [Fact]
    public void A_gauges_border_and_ornaments_keep_clear_of_its_pin_holes()
    {
        var k = Make(Gauge(MarkDesigns.Apply(new Markings(), "Medieval")));
        var pin = Measures.Pin(3, 0.1);
        foreach (var p in k.Parts.Where(p => p.Name.StartsWith("Segment")))
        {
            // The pin holes are at the joints: x = 0 (but not on the first) and x = one segment along (but not on the last).
            var near = new PathsD();
            if (p.Name != "Segment 1") near.Add(Geo.Circle(0, 0, pin.Hole / 2 + 0.5, 0.01));
            if (p.Name != "Segment 4") near.Add(Geo.Circle(25.4, 0, pin.Hole / 2 + 0.5, 0.01));
            Assert.True(Geo.Area(Geo.Intersect(p.Etched, near)) < 0.01, p.Name);
        }
    }

    [Fact]
    public void Numbers_can_be_roman_pips_tally_marks_with_the_unit_or_none()
    {
        if (!HaveFonts) return;
        Part Seg(Numbering n, int i) => Make(Gauge(new Markings { Numbers = n }, 6)).Parts.Single(p => p.Name == $"Segment {i}");
        // Three pips on the third segment, five on the fifth.
        Assert.Equal(3, Geo.Tidy(Seg(Numbering.Pips, 3).Etched).Count);
        Assert.Equal(5, Geo.Tidy(Seg(Numbering.Pips, 5).Etched).Count);
        // Five tally marks: four strokes and one across, joined where they cross.
        Assert.True(Seg(Numbering.Tally, 5).Etched.Count > 0);
        Assert.True(Geo.Area(Seg(Numbering.WithUnit, 2).Etched) > Geo.Area(Seg(Numbering.Numbers, 2).Etched));
        Assert.Empty(Seg(Numbering.None, 2).Etched);
        Assert.Equal("XIV", MarkDesigns.Roman(14));
        Assert.Equal("MCMXC", MarkDesigns.Roman(1990));
        // Every other number.
        var every = Make(Gauge(new Markings { Every = 2 }, 6));
        Assert.Empty(every.Parts.Single(p => p.Name == "Segment 3").Etched);
        Assert.NotEmpty(every.Parts.Single(p => p.Name == "Segment 4").Etched);
    }

    [Fact]
    public void Outlined_and_single_line_marks_are_scored_not_filled()
    {
        if (!HaveFonts) return;
        var engraved = Make(Gauge(new Markings())).Parts.Single(p => p.Name == "Segment 2");
        var outlined = Make(Gauge(new Markings { Look = MarkLook.Outlined })).Parts.Single(p => p.Name == "Segment 2");
        var single = Make(Gauge(new Markings { Look = MarkLook.SingleLine })).Parts.Single(p => p.Name == "Segment 2");
        Assert.NotEmpty(engraved.Etched);
        Assert.Empty(engraved.Lines);
        Assert.Empty(outlined.Etched);
        Assert.NotEmpty(outlined.Lines);
        Assert.Empty(single.Etched);
        Assert.NotEmpty(single.Lines);
        // Single lines: one pass down each stroke, so far less line than going round it.
        Assert.True(single.Lines.Sum(Length) < outlined.Lines.Sum(Length));
    }

    static double Length(PathD l)
    {
        double d = 0;
        for (int i = 1; i < l.Count; i++) d += Math.Sqrt((l[i].x - l[i - 1].x) * (l[i].x - l[i - 1].x) + (l[i].y - l[i - 1].y) * (l[i].y - l[i - 1].y));
        return d;
    }

    [Fact]
    public void A_title_goes_on_the_first_segment_the_ruler_and_round_the_circle()
    {
        if (!HaveFonts) return;
        var plain = Make(Gauge(new Markings()));
        var titled = Make(Gauge(new Markings { Title = "MOVE" }));
        Assert.True(Geo.Area(titled.Parts[0].Etched) > Geo.Area(plain.Parts[0].Etched) * 1.5);
        Assert.Contains(titled.Findings, f => f.Text.Contains("MOVE"));
        // The same on the other segments.
        Assert.Equal(Geo.Area(plain.Parts[2].Etched), Geo.Area(titled.Parts[2].Etched), 3);

        var ruler = Make(Defaults.For(AccessoryKind.Ruler) with { Marks = new Markings { Title = "RANGE" } }).Parts[0];
        var bare = Make(Defaults.For(AccessoryKind.Ruler)).Parts[0];
        Assert.True(Geo.Area(ruler.Etched) > Geo.Area(bare.Etched));

        var circle = Make(Defaults.For(AccessoryKind.Template) with { LengthUnits = 2, Marks = new Markings { Title = "FIREBALL" } }).Parts[0];
        // Round the top: above the middle, in the outer band.
        var top = Geo.Intersect(circle.Etched, [Geo.Rect(0, 50.8 + 25.4 + 2, 101.6, 101.6)]);
        Assert.True(Geo.Area(top) > 20);
    }

    [Fact]
    public void Ornaments_go_at_the_ends_beside_the_title_or_with_every_number()
    {
        var mk = new Markings { Ornament = "Star", Numbers = Numbering.None };
        var ends = Make(Gauge(mk with { Place = OrnamentPlace.Ends }));
        Assert.NotEmpty(ends.Parts.Single(p => p.Name == "Segment 1").Etched);
        Assert.Empty(ends.Parts.Single(p => p.Name == "Segment 2").Etched);
        Assert.NotEmpty(ends.Parts.Single(p => p.Name == "Segment 4").Etched);
        var every = Make(Gauge(mk with { Place = OrnamentPlace.EveryNumber }));
        Assert.All(every.Parts.Where(p => p.Name.StartsWith("Segment")), p => Assert.NotEmpty(p.Etched));
        // An arrow at the ends points outward: the first segment's points left, the last's right.
        var arrows = Make(Gauge(new Markings { Ornament = "Arrow", Numbers = Numbering.None }));
        // The arrow's tip is on the middle line; its feathers are off it.
        var first = arrows.Parts.Single(p => p.Name == "Segment 1").Etched.SelectMany(q => q).MinBy(q => q.x);
        var last = arrows.Parts.Single(p => p.Name == "Segment 4").Etched.SelectMany(q => q).MaxBy(q => q.x);
        Assert.True(Math.Abs(first.y) < 0.3, $"first segment's arrow: {first.y}");
        Assert.True(Math.Abs(last.y) < 0.3, $"last segment's arrow: {last.y}");
    }

    [Fact]
    public void Lines_stop_short_of_the_numbers_on_templates()
    {
        if (!HaveFonts) return;
        var sq = Make(Defaults.For(AccessoryKind.Template) with { LengthUnits = 2, Shape = TemplateShape.Square }).Parts[0];
        var clear = Geo.Offset(sq.Etched, 0.4);
        Assert.DoesNotContain(Geo.ClipLines(sq.Lines, clear), l => Spread(l) > 0.05);
    }

    [Fact]
    public void A_unit_of_your_own_sets_the_length_and_the_numbers()
    {
        var it = Defaults.For(AccessoryKind.MoveGauge) with { Units = Units.Custom, UnitMm = 31.75, UnitCounts = 2, UnitName = "hexes", Segments = 4, StepUnits = 1 };
        var k = Make(it);
        Assert.True(k.Ok);
        var b = Geo.Bounds(k.Parts.Single(p => p.Name == "Segment 1").Cut);
        // One unit, and the round end over the next segment.
        Assert.Equal(31.75 + 6, b.right - b.left, 2);
        Assert.Equal("4", Defaults.Mark(it, 2));
        Assert.Equal(" hexes", Defaults.UnitSuffix(it));
        Assert.Contains(k.Findings, f => f.Text.Contains("hexes"));
        var ruler = Defaults.For(AccessoryKind.Ruler) with { Units = Units.Custom, UnitMm = 20, UnitName = "MU", LengthUnits = 5 };
        var r = Make(ruler).Parts[0];
        Assert.Equal(100, Geo.Bounds(r.Cut).right, 3);
        // Quarters of 20 mm, the far end left off.
        Assert.Equal(19, r.Lines.Count(l => l.Count == 2 && Math.Abs(l[0].x - l[1].x) < 1e-9));
    }

    [Fact]
    public void The_finest_ticks_can_be_set_and_are_never_crowded()
    {
        int Ticks(AccessoryItem it) => Make(it).Parts[0].Lines.Count(l => l.Count == 2 && Math.Abs(l[0].x - l[1].x) < 1e-9 && Math.Max(l[0].y, l[1].y) > it.WidthMm - 0.01);
        var inch = Defaults.For(AccessoryKind.Ruler) with { LengthUnits = 6 };
        Assert.Equal(6 * 4 - 1, Ticks(inch));
        Assert.Equal(6 * 16 - 1, Ticks(inch with { Marks = new Markings { Ticks = 16 } }));
        Assert.Equal(6 * 2 - 1, Ticks(inch with { Marks = new Markings { Ticks = 2 } }));
        // Tenths of a 3 mm unit would be 0.3 mm apart: halved until they're far enough apart.
        Assert.Equal(1, Measures.TickParts(Defaults.For(AccessoryKind.Ruler) with { Units = Units.Custom, UnitMm = 1, Marks = new Markings { Ticks = 10 } }));
        Assert.Equal(5, Measures.TickParts(Defaults.For(AccessoryKind.Ruler) with { Units = Units.Custom, UnitMm = 3, Marks = new Markings { Ticks = 10 } }));
    }

    [Fact]
    public void An_uploaded_drawing_keeps_its_holes_and_lines()
    {
        var dir = Directory.CreateTempSubdirectory("gamekit-art-").FullName;
        try
        {
            var svg = Path.Combine(dir, "ring.svg");
            File.WriteAllText(svg, """
                <svg xmlns="http://www.w3.org/2000/svg" width="40mm" height="20mm" viewBox="0 0 40 20">
                  <path d="M0 0 H20 V20 H0 Z M5 5 V15 H15 V5 Z" fill="black"/>
                  <path d="M25 10 L40 10" stroke="black" fill="none"/>
                </svg>
                """);
            var art = MarkArts.FromFile(svg);
            Assert.Equal("ring", art.Name);
            Assert.Equal(2, art.Aspect, 1);
            var (areas, lines) = MarkArts.Unit(art);
            // The square with a square hole: 20² − 10², a quarter hole.
            var b = Geo.Bounds(areas);
            Assert.Equal(0.75 * (b.right - b.left) * (b.bottom - b.top), Geo.Area(areas), 1);
            Assert.Single(lines);
            Assert.Equal(2, Geo.Tidy(areas).Count);

            // Kept with the settings, and as a file to pick again.
            var set = new AccessorySet { Items = [Gauge(new Markings { Ornament = Ornaments.ArtName, Art = art, Border = BorderStyle.Art, BorderArt = art })] };
            var back = AccessorySet.FromJson(set.ToJson())!;
            Assert.Equal(art, back.Items[0].Marks.Art);
            var store = new ArtStore(Path.Combine(dir, "designs"));
            var saved = store.Save(art);
            var again = store.Save(art);
            Assert.Equal("ring", saved.Name);
            Assert.Equal("ring 2", again.Name);
            Assert.Equal(2, store.All().Count);
            store.Delete("ring 2");
            Assert.Single(store.All());

            // As the ornament and along the border.
            var k = GameKitMaker.Make(back, 210);
            Assert.True(k.Ok);
            var seg = k.Items[0].Parts.Single(p => p.Name == "Segment 2");
            var none = Make(Gauge(new Markings())).Parts.Single(p => p.Name == "Segment 2");
            Assert.True(Geo.Area(seg.Etched) > Geo.Area(none.Etched) + 5);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void An_uploaded_picture_is_traced()
    {
        var dir = Directory.CreateTempSubdirectory("gamekit-pic-").FullName;
        try
        {
            // A black disc on white, and a transparent corner.
            int w = 200, h = 200;
            var rgba = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = (y * w + x) * 4;
                    bool ink = (x - 100) * (x - 100) + (y - 100) * (y - 100) < 70 * 70;
                    byte v = ink ? (byte)0 : (byte)255;
                    rgba[i] = rgba[i + 1] = rgba[i + 2] = v;
                    rgba[i + 3] = x < 20 && y < 20 ? (byte)0 : (byte)255;
                }
            var png = Path.Combine(dir, "disc.png");
            File.WriteAllBytes(png, new RasterImage(w, h, rgba).ToPng());
            var art = MarkArts.FromFile(png);
            var s = MarkArts.Shape(art);
            Assert.Single(Geo.Tidy(s));
            // A disc fills π/4 of its box.
            Assert.Equal(Math.PI, Geo.Area(s), 0);
            Assert.Throws<FormatException>(() => MarkArts.FromGeometry("empty", PathGeometry.Empty));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Handwriting_fonts_write_the_numbers_as_single_lines()
    {
        var dir = Directory.CreateTempSubdirectory("gamekit-hand-").FullName;
        var before = HandFonts.Folder;
        try
        {
            var fonts = Path.Combine(dir, "handwriting", "fonts");
            Directory.CreateDirectory(fonts);
            // A tiny font: every digit a stroke down and one across.
            var glyphs = string.Join(",", "0123456789".Select(c => $$"""{"char":"{{c}}","advance":0.6,"strokes":[{"closed":false,"points":[0,-1,0.5,0]},{"closed":false,"points":[0,-0.5,0.6,-0.5]}]}"""));
            File.WriteAllText(Path.Combine(fonts, "Mine.json"), $$"""{"format":1,"kind":"photoncanvas-handwriting-font","name":"Mine","glyphs":[{{glyphs}}]}""");
            HandFonts.Folder = HandFonts.FolderBeside(Path.Combine(dir, "gamekit"));
            Assert.Contains(HandFonts.All(), f => f.Name == "Mine");
            var mk = new Markings { Font = HandFonts.Prefix + "Mine", Look = MarkLook.SingleLine };
            var seg = Make(Gauge(mk)).Parts.Single(p => p.Name == "Segment 3");
            Assert.Empty(seg.Etched);
            Assert.Equal(2, seg.Lines.Count);
            // Engraved: the strokes drawn thick.
            var thick = Make(Gauge(mk with { Look = MarkLook.Engraved })).Parts.Single(p => p.Name == "Segment 3");
            Assert.NotEmpty(thick.Etched);
            // A font that isn't here: the sheet's font instead, and a note saying so.
            var missing = Make(Gauge(new Markings { Font = HandFonts.Prefix + "Lost" }));
            Assert.True(missing.Ok);
            Assert.Contains(missing.Findings, f => f.Level == Severity.Warn && f.Text.Contains("Lost"));
        }
        finally
        {
            HandFonts.Folder = before;
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void A_theme_gives_gauges_rulers_and_templates_its_design()
    {
        var set = new AccessorySet { Items = [Defaults.For(AccessoryKind.DiceTower), Gauge(new Markings { Title = "MOVE" }), Defaults.For(AccessoryKind.Template)] };
        var dungeon = GameKitThemes.Apply(set, "Dungeon");
        Assert.Equal(BorderStyle.Rivets, dungeon.Items[1].Marks.Border);
        Assert.Equal("Skull", dungeon.Items[2].Marks.Ornament);
        Assert.Equal("MOVE", dungeon.Items[1].Marks.Title);
        Assert.Equal(new Markings(), dungeon.Items[0].Marks);
        Assert.Equal(BorderStyle.Circuit, GameKitThemes.Apply(set, "Sci-fi").Items[1].Marks.Border);
    }

    [Fact]
    public void Settings_saved_before_markings_open_plain()
    {
        var old = """{"Theme":"Medieval","Items":[{"Kind":"MoveGauge","Segments":6,"StepUnits":1,"WidthMm":12,"Units":"Inches"}],"ThicknessMm":3}""";
        var set = AccessorySet.FromJson(old)!;
        Assert.Equal(new Markings(), set.Items[0].Marks);
        Assert.True(GameKitMaker.Make(set, 210).Ok);
        var marked = set with { Items = [set.Items[0] with { Marks = MarkDesigns.Apply(new Markings(), "Gothic") with { Font = "Georgia", Every = 2, Look = MarkLook.Outlined } }] };
        Assert.Equal(marked.Items[0].Marks, AccessorySet.FromJson(marked.ToJson())!.Items[0].Marks);
    }

    [Fact]
    public void Every_design_has_a_picture_to_pick_it_by()
    {
        foreach (var d in MarkDesigns.All)
        {
            var (cut, etched, lines) = Measures.Sample(MarkDesigns.Apply(new Markings(), d.Name), "Arial", 34, 12.5);
            Assert.NotEmpty(cut);
            if (d.Border != BorderStyle.None || d.Ornament.Length > 0) Assert.True(etched.Count + lines.Count > 0, d.Name);
        }
    }
}
