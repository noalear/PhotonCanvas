using PhotonCanvas.Addon.HouseEditor;

namespace PhotonCanvas.Tests;

/// <summary>The building editor keeps what it made (walls, tiles, floor patterns) so a change only remakes what it touches, and comes out just the same.</summary>
public class HouseEditorSpeedTests
{
    static string Fingerprint(HouseKit kit)
    {
        var h = new KitCache.Hash();
        foreach (var p in kit.Pieces) h.Add(p.Cut).Add(p.Etched).Add(p.Lines).Add(p.Marks).Add((ulong)p.Copies).Add((ulong)p.Number).Add((ulong)p.Name.GetHashCode(StringComparison.Ordinal));
        foreach (var s in kit.Solids) h.Add(s.Outline).Add(s.Etched ?? []).Add(s.Lines ?? []).Add(s.Pose.O.X).Add(s.Pose.O.Y).Add(s.Pose.O.Z).Add((ulong)s.Number);
        return $"{h}|{string.Join("\n", kit.Findings.Select(f => f.Text))}";
    }

    [Fact]
    public void A_change_made_from_what_was_kept_comes_out_as_if_made_from_scratch()
    {
        var d = HouseDesign.Sample();
        var lv = d.Levels[0];
        var o = lv.Openings[^1];
        var moved = d with { Levels = [lv with { Openings = [.. lv.Openings.SkipLast(1), o.X % 1 == 0 ? o with { Y = o.Y + 0.25 } : o with { X = o.X + 0.25 }] }, .. d.Levels.Skip(1)] };

        HouseKitMaker.Make(d);
        var fromKept = HouseKitMaker.Make(moved);
        KitCache.Clear();
        var fresh = HouseKitMaker.Make(moved);

        Assert.Equal(Fingerprint(fresh), Fingerprint(fromKept));    }
}

/// <summary>The building editor's 3D view: a single-panel wall's other side shows, and a banana for scale lies where it's put without being cut.</summary>
public class HouseEditorViewTests
{
    [Fact]
    public void A_single_panel_walls_other_side_is_in_the_3D_model()
    {
        var d = HouseDesign.Starter();
        var lv = d.Levels[0];
        var looks = lv.Walls.Select(e => new WallLook { Edge = e, Texture = "Fieldstone", BackTexture = "Timber frame" }).ToList();
        var flat = d with { Style = d.Style with { BoxWalls = false }, Levels = [lv with { WallLooks = looks }] };
        var kit = HouseKitMaker.Make(flat);
        Assert.True(kit.Ok, string.Join("\n", kit.Findings.Select(f => f.Text)));
        var walls = kit.Solids.Where(s => s.Kind == SolidKind.Wall).ToList();
        Assert.NotEmpty(walls);
        Assert.All(walls, w => Assert.True(w.BackEtched is { Count: > 0 } || w.BackLines is { Count: > 0 }, $"{w.Name} has no other side"));
        // Without one of its own, a single panel's back is left plain.
        var plain = HouseKitMaker.Make(flat with { Levels = [lv] });
        Assert.All(plain.Solids.Where(s => s.Kind == SolidKind.Wall), w => Assert.Null(w.BackEtched));
    }

    [Fact]
    public void A_banana_for_scale_is_shown_but_not_cut()
    {
        var d = HouseDesign.Starter();
        var lv = d.Levels[0];
        var plain = HouseKitMaker.Make(d);
        var withBanana = HouseKitMaker.Make(d with { Levels = [lv with { Furniture = [new PlacedItem { X = 13, Y = 13, Turn = 1, Banana = true }] }] });
        var banana = Assert.Single(withBanana.Props);
        Assert.Equal("Banana for scale", banana.Name);
        Assert.Equal(90, banana.TurnDeg);
        Assert.Equal(plain.PieceCount, withBanana.PieceCount);
        Assert.Equal(plain.Solids.Count, withBanana.Solids.Count);
        // It takes about a real banana's room on the plan, turned as it lies.
        var r = HouseKitMaker.ItemArea(d.Style, new PlacedItem { X = 13, Y = 13, Turn = 1, Banana = true }, d.SquareMm);
        Assert.Equal(HouseKitMaker.BananaLongMm, (r.bottom - r.top) * d.SquareMm, 3);
        // Saved with the design.
        var back = HouseDesign.FromJson((d with { Levels = [lv with { Furniture = [new PlacedItem { Banana = true }] }] }).ToJson());
        Assert.True(back!.Levels[0].Furniture[0].Banana);
    }
}
