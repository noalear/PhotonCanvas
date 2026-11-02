using Clipper2Lib;
using PhotonCanvas.Addon.Roofs;
using PhotonCanvas.Addon.Walls;
using Xunit;

namespace PhotonCanvas.Tests;

public class RoofsTests
{
    static RoofPart Part(RoofKit k, string name) => k.Parts.Single(p => p.Name == name);

    /// <summary>A point inside a piece's material (shrunk a little, so touching faces don't count).</summary>
    static bool InMaterial(RoofPart p, PathsD shrunk, Pose pose, double T, V3 q)
    {
        var d = q - pose.O;
        double u = d.Dot(pose.U), v = d.Dot(pose.V), w = d.Dot(pose.N);
        if (w <= 0.06 || w >= T - 0.06) return false;
        return Clipper.PointInPolygon(new PointD(u, v), shrunk.OrderByDescending(x => Math.Abs(Clipper.Area(x))).First()) == PointInPolygonResult.IsInside
            && Geo.Area(Geo.Intersect(shrunk, [Geo.Rect(u - 0.005, v - 0.005, u + 0.005, v + 0.005)])) > 1e-5;
    }

    /// <summary>Pieces that run into each other when put together: points through each piece's middle that are inside another piece.</summary>
    static List<string> Clashes(RoofKit kit, double step = 2.5)
    {
        double T = kit.ThicknessMm;
        var placed = kit.Parts.SelectMany(p => p.Poses.Select((pose, i) => (Part: p, Pose: pose, Name: $"{p.Name} #{i + 1}", Shrunk: Geo.Offset(p.Cut, -0.08)))).ToList();
        var problems = new List<string>();
        foreach (var a in placed)
        {
            var (x0, y0, x1, y1) = Shapes.Box(a.Part.Cut);
            var inside = Geo.Offset(a.Part.Cut, -0.3);
            var seen = new HashSet<string>();
            for (double x = x0 + step / 2; x < x1; x += step)
                for (double y = y0 + step / 2; y < y1; y += step)
                {
                    if (Geo.Area(Geo.Intersect(inside, [Geo.Rect(x - 0.005, y - 0.005, x + 0.005, y + 0.005)])) < 1e-5) continue;
                    var q = a.Pose.At(x, y, T / 2);
                    foreach (var b in placed)
                        if (!ReferenceEquals(a.Part, b.Part) || a.Pose != b.Pose)
                            if (b.Shrunk.Count > 0 && !seen.Contains(b.Name) && InMaterial(b.Part, b.Shrunk, b.Pose, T, q))
                            {
                                seen.Add(b.Name);
                                problems.Add($"{a.Name} runs into {b.Name} at ({q.X:0.#}, {q.Y:0.#}, {q.Z:0.#})");
                            }
                }
        }
        return problems;
    }

    [Fact]
    public void The_default_roof_fits_the_default_walls_room()
    {
        var fitted = RoofMaker.FitToWalls(new RoofSpec(), new WallSpec());
        // 100 mm panels, 16 mm columns with 4 mm slots: centres 108.5 apart, plus a column and the caps' 2 mm lip.
        Assert.Equal(128.5, fitted.LengthMm, 2);
        Assert.Equal(128.5, fitted.WidthMm, 2);
        Assert.Equal(11.5, fitted.InsetMm, 2);
        Assert.Equal(20, fitted.CornerMm, 2);
        // 17 layers of 3 mm and a cap: 54 mm, over a 50 mm wall.
        Assert.Equal(4, fitted.RestAboveWallsMm, 2);
        Assert.Equal(new RoofSpec().LengthMm, fitted.LengthMm, 2);
    }

    [Fact]
    public void Two_sets_of_walls_make_a_rectangular_roof_with_the_ridge_along_the_longer()
    {
        var fitted = RoofMaker.FitToWalls(new RoofSpec(), new WallSpec { LengthMm = 80, Texture = "Timber frame" }, new WallSpec { LengthMm = 150 });
        Assert.Equal(178.5, fitted.LengthMm, 2);
        Assert.Equal(108.5, fitted.WidthMm, 2);
    }

    [Fact]
    public void The_gables_take_the_walls_texture()
    {
        var fitted = RoofMaker.FitToWalls(new RoofSpec(), new WallSpec { Texture = "Timber frame", SizeA = 3, SizeB = 20 });
        Assert.Equal("Timber frame", fitted.GableTexture);
        Assert.Equal(3, fitted.GableA);
    }

    [Theory]
    [InlineData(RoofStyle.Gable, 45)]
    [InlineData(RoofStyle.Gable, 30)]
    [InlineData(RoofStyle.Gable, 60)]
    [InlineData(RoofStyle.Hip, 40)]
    [InlineData(RoofStyle.LeanTo, 25)]
    public void A_roof_goes_together_without_pieces_running_into_each_other(RoofStyle style, double pitch)
    {
        var kit = RoofMaker.Make(new RoofSpec { Style = style, PitchDeg = pitch, LengthMm = 150, WidthMm = 110, Shingle = Shingles.Plain, GableTexture = Textures.Plain });
        Assert.True(kit.Ok, string.Join("\n", kit.Findings));
        var clashes = Clashes(kit);
        Assert.True(clashes.Count == 0, string.Join("\n", clashes));
    }

    [Fact]
    public void A_pyramid_roof_goes_together()
    {
        var kit = RoofMaker.Make(new RoofSpec { Style = RoofStyle.Hip, PitchDeg = 50, LengthMm = 90, WidthMm = 90, Shingle = Shingles.Plain });
        Assert.True(kit.Ok, string.Join("\n", kit.Findings));
        Assert.Contains(kit.Parts, p => p.Name == "Cross board");
        Assert.Equal(2, Part(kit, "End slope").Copies);
        var clashes = Clashes(kit);
        Assert.True(clashes.Count == 0, string.Join("\n", clashes));
    }

    [Fact]
    public void A_hipped_roof_wider_than_long_turns_its_ridge()
    {
        var kit = RoofMaker.Make(new RoofSpec { Style = RoofStyle.Hip, LengthMm = 80, WidthMm = 140, Shingle = Shingles.Plain });
        var ridge = Part(kit, "Ridge board").Poses[0];
        Assert.Equal(0, ridge.U.X, 6);
        Assert.Equal(1, Math.Abs(ridge.U.Y), 6);
        // The base still covers 80 along X and 140 along Y.
        var basePose = Part(kit, "Base").Poses[0];
        var (x0, y0, x1, y1) = Shapes.Box(Part(kit, "Base").Cut);
        var a = basePose.At(x0, y0);
        var b = basePose.At(x1, y1);
        Assert.Equal(80, Math.Abs(b.X - a.X), 3);
        Assert.Equal(140, Math.Abs(b.Y - a.Y), 3);
        Assert.Equal(0, Math.Min(a.X, b.X), 3);
    }

    [Fact]
    public void Every_tab_has_a_slot_in_the_base()
    {
        var kit = RoofMaker.Make(new RoofSpec { Shingle = Shingles.Plain });
        var basePart = Part(kit, "Base");
        // The base has a hole for each tab: two per gable and per truss.
        int holes = Geo.Tidy(basePart.Cut).Count(p => Clipper.Area(p) < 0);
        int standing = Part(kit, "Gable end").Copies + (kit.Parts.FirstOrDefault(p => p.Name == "Truss")?.Copies ?? 0);
        Assert.Equal(2 * standing, holes);
    }

    [Fact]
    public void The_locator_stays_inside_the_walls_and_clear_of_the_columns()
    {
        var spec = RoofMaker.FitToWalls(new RoofSpec(), new WallSpec());
        var kit = RoofMaker.Make(spec);
        var loc = Part(kit, "Locator");
        var (x0, y0, x1, y1) = Shapes.Box(loc.Cut);
        Assert.True(x0 >= spec.InsetMm, $"{x0}");
        Assert.True(spec.LengthMm - x1 >= spec.InsetMm, $"{x1}");
        // Nothing in the corner squares where the columns are.
        Assert.Equal(0, Geo.Area(Geo.Intersect(loc.Cut, [Geo.Rect(0, 0, spec.CornerMm, spec.CornerMm)])), 3);
        // 4 mm above the walls plus 2 mm of grip: two 3 mm layers.
        Assert.Equal(2, loc.Copies);
        Assert.Equal(1, Geo.Pieces(loc.Cut));
    }

    [Theory]
    [InlineData("Slate")]
    [InlineData("Wooden shingles")]
    [InlineData("Fish scale")]
    [InlineData("Clay tiles")]
    [InlineData("Thatch")]
    [InlineData("Wood planks")]
    [InlineData("Corrugated metal")]
    public void Every_shingle_preset_etches_inside_the_slopes(string shingle)
    {
        var p = Shingles.Preset(shingle)!;
        var kit = RoofMaker.Make(new RoofSpec { Shingle = shingle, ShingleA = p.SizeA, ShingleB = p.SizeB, ShingleGrooveMm = p.Groove, GableTexture = Textures.Plain });
        var slope = Part(kit, "Slope (right)");
        double area = Geo.Area(slope.Cut), etched = Geo.Area(slope.Etched);
        Assert.True(etched > 0.02 * area || slope.Lines.Count > 50, $"{shingle}: {etched:0} mm² etched, {slope.Lines.Count} lines");
        Assert.True(etched < 0.8 * area, $"{shingle}: nearly all etched");
        Assert.Equal(0, Geo.Area(Geo.Subtract(slope.Etched, slope.Cut)), 2);
    }

    [Fact]
    public void Slates_lie_in_courses_from_the_eave()
    {
        // 6 × 4.5 mm slates with 0.5 mm gaps: about (6·4.5 − 5.5·4) / (6·4.5) ≈ 19 % of the slope is etched.
        var kit = RoofMaker.Make(new RoofSpec { Shingle = "Slate", ShingleA = 6, ShingleB = 4.5, ShingleGrooveMm = 0.5 });
        var slope = Part(kit, "Slope (right)");
        Assert.Equal(0.185, Geo.Area(slope.Etched) / Geo.Area(slope.Cut), 1);
    }

    [Fact]
    public void A_gable_window_is_cut_through_the_gables()
    {
        var with = Part(RoofMaker.Make(new RoofSpec { Window = GableWindow.Arched, WindowMm = 12 }), "Gable end");
        var without = Part(RoofMaker.Make(new RoofSpec { Window = GableWindow.None }), "Gable end");
        Assert.True(Geo.Area(without.Cut) - Geo.Area(with.Cut) > 100);
    }

    [Fact]
    public void Settings_survive_being_saved()
    {
        var spec = new RoofSpec { Style = RoofStyle.Hip, Shingle = "Thatch", PitchDeg = 50, GableCustom = new CustomTexture { Name = "Mine", Path = "M0,0 L1,1" } };
        var back = RoofsDesign.FromJson(spec.ToJson())!.Roof!;
        Assert.Equal(spec with { GableCustom = null }, back with { GableCustom = null });
        Assert.Equal("Mine", back.GableCustom!.Name);
        var tower = new TowerSpec { Join = ConnectorKind.Tee, Top = TowerTop.Cone };
        Assert.Equal(tower, RoofsDesign.FromJson(tower.ToJson())!.Tower);
        Assert.Null(RoofsDesign.FromJson("{}"));
    }

    // ---- cones and towers ----

    [Fact]
    public void A_cone_roof_steps_in_with_the_pitch()
    {
        var kit = RoofMaker.Make(new RoofSpec { Style = RoofStyle.Cone, WidthMm = 60, PitchDeg = 60, EaveMm = 4, InsetMm = 8 });
        Assert.True(kit.Ok, string.Join("\n", kit.Findings));
        var rings = kit.Parts.Where(p => p.Name.StartsWith("Cone ring")).ToList();
        // From 34 mm out, 3 / tan 60° ≈ 1.73 mm in per ring: about 19 rings.
        Assert.InRange(rings.Count, 17, 21);
        var r0 = Shapes.Box(rings[0].Cut);
        Assert.Equal(68, r0.X1 - r0.X0, 0);
        Assert.Contains(kit.Parts, p => p.Name == "Locator ring");
        var clashes = Clashes(kit, 2);
        Assert.True(clashes.Count == 0, string.Join("\n", clashes));
    }

    [Fact]
    public void A_tower_is_stacked_rings_with_floors_and_battlements()
    {
        var kit = TowerMaker.Make(new TowerSpec());
        Assert.True(kit.Ok, string.Join("\n", kit.Findings));
        // 100 mm of 3 mm layers.
        Assert.Equal(33, kit.Parts.Count(p => p.Name.StartsWith("Ring") || p.Name.Contains("(layer")));
        Assert.Contains(kit.Parts, p => p.Name.StartsWith("Ground floor"));
        Assert.Contains(kit.Parts, p => p.Name.StartsWith("Deck"));
        // The top rings have the battlements' gaps: they come in pieces.
        Assert.Contains(kit.Parts, p => p.Name == "Ring 33" || p.Name.StartsWith("Ring 33 ("));
        Assert.True(Geo.Pieces(kit.Parts.Single(p => p.Name.StartsWith("Ring 33")).Cut) > 3);
        var clashes = Clashes(kit, 3);
        Assert.True(clashes.Count == 0, string.Join("\n", clashes));
    }

    [Fact]
    public void Stonework_notches_the_rings_edges()
    {
        var plain = TowerMaker.Make(new TowerSpec { Texture = Textures.Plain, Door = false, WindowsPerStorey = 0 });
        var stone = TowerMaker.Make(new TowerSpec { Texture = "Brick", SizeA = 8, SizeB = 3, GrooveMm = 0.8, Door = false, WindowsPerStorey = 0 });
        double a = Geo.Area(plain.Parts.Single(p => p.Name == "Ring 2").Cut), b = Geo.Area(stone.Parts.Single(p => p.Name == "Ring 2").Cut);
        Assert.True(a - b > 5, $"{a:0.#} vs {b:0.#}");
        // Brick: the joints move half a brick from one course to the next.
        var r2 = stone.Parts.Single(p => p.Name == "Ring 2").Cut;
        var r3 = stone.Parts.Single(p => p.Name == "Ring 3").Cut;
        Assert.True(Geo.Area(Geo.Subtract(r2, r3)) > 1);
    }

    [Fact]
    public void A_door_and_windows_are_cut_through_the_rings()
    {
        var kit = TowerMaker.Make(new TowerSpec { Door = true, DoorWidthMm = 14, DoorHeightMm = 24, WindowsPerStorey = 2 });
        // The door: layers 2 to 9 (3 to 27 mm) open at the front.
        var ring4 = kit.Parts.Single(p => p.Name.StartsWith("Ring 4"));
        Assert.Equal(0, Geo.Area(Geo.Intersect(ring4.Cut, [Geo.Rect(-5, -30, 5, -24)])), 2);
        Assert.Contains(kit.Findings, f => f.Text.Contains("pieces"));
    }

    [Fact]
    public void A_tower_takes_the_walls_in_slots_as_high_as_the_walls()
    {
        var walls = new WallSpec { HeightMm = 45 };
        var spec = WallsFit.Fit(new TowerSpec(), walls) with { Door = false, WindowsPerStorey = 0 };
        Assert.Equal(ConnectorKind.Corner, spec.Join);
        var kit = TowerMaker.Make(spec);
        Assert.True(kit.Ok, string.Join("\n", kit.Findings));
        double R = spec.DiameterMm / 2;
        bool Slotted(string name, double deg)
        {
            var cut = kit.Parts.Single(p => p.Name.StartsWith(name)).Cut;
            double a = deg * Math.PI / 180, r = R - spec.SlotDepthMm / 2;
            var probe = Geo.Rect(r * Math.Cos(a) - 0.2, r * Math.Sin(a) - 0.2, r * Math.Cos(a) + 0.2, r * Math.Sin(a) + 0.2);
            return Geo.Area(Geo.Intersect(cut, [probe])) < 1e-3;
        }
        Assert.True(Slotted("Ring 5", 0));
        Assert.True(Slotted("Ring 5", 90));
        Assert.False(Slotted("Ring 5", 45));
        // 45 mm of wall: the first 15 layers have the slot (the 15th is a floor); the 20th hasn't.
        Assert.True(Slotted("Ring 13", 0));
        Assert.False(Slotted("Ring 20", 0));
    }

    [Fact]
    public void A_cone_topped_tower_goes_together()
    {
        var kit = TowerMaker.Make(new TowerSpec { Top = TowerTop.Cone, HeightMm = 60, Storeys = 1 });
        Assert.True(kit.Ok, string.Join("\n", kit.Findings));
        Assert.Contains(kit.Parts, p => p.Name == "Roof locator ring");
        var clashes = Clashes(kit, 3);
        Assert.True(clashes.Count == 0, string.Join("\n", clashes));
    }
}
