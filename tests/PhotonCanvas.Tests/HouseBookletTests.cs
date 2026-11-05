using Clipper2Lib;
using PhotonCanvas.Addon.Buildings;
using PhotonCanvas.Addon.HouseEditor;
using Geo = PhotonCanvas.Addon.Walls.Geo;

namespace PhotonCanvas.Tests;

/// <summary>The building editor's piece numbers and building booklet: every piece numbered in the order it goes on, its number scored on it, and every piece accounted for in the steps.</summary>
public class HouseBookletTests
{
    static readonly Lazy<(HouseDesign Design, HouseKit Kit)> Sample = new(() =>
    {
        var d = HouseDesign.Sample();
        return (d, HouseKitMaker.Make(d, longestMm: 400));
    });

    [Fact]
    public void Every_piece_is_numbered_in_the_order_it_goes_on()
    {
        var (_, kit) = Sample.Value;
        Assert.True(kit.Ok, string.Join("\n", kit.Findings.Where(f => f.Level == Severity.Bad).Select(f => f.Text)));
        Assert.All(kit.Pieces, p => Assert.True(p.Number > 0, $"{p.Name} has no number"));
        // 1, 2, 3 … with none missed.
        var numbers = kit.Pieces.Select(p => p.Number).Distinct().Order().ToList();
        Assert.Equal(Enumerable.Range(1, numbers.Count), numbers);
        // The pieces in the 3D model carry their pieces' numbers; nearly all of them are found.
        Assert.True(kit.Solids.Count(s => s.Number > 0) >= kit.Solids.Count * 0.95, $"{kit.Solids.Count(s => s.Number == 0)} of {kit.Solids.Count} pieces in the model have no number");
        // The ground goes on first and the roofs last.
        int Lowest(SolidKind k) => kit.Solids.Where(s => s.Kind == k && s.Number > 0).Min(s => s.Number);
        int Highest(SolidKind k) => kit.Solids.Where(s => s.Kind == k && s.Number > 0).Max(s => s.Number);
        Assert.True(Highest(SolidKind.Ground) < Lowest(SolidKind.Wall));
        Assert.True(Highest(SolidKind.Wall) < Lowest(SolidKind.Roof) || kit.Solids.Any(s => s.Kind == SolidKind.Wall && s.Level > 0));
        Assert.Equal(kit.Pieces.Max(p => p.Number), kit.Solids.Where(s => s.Kind == SolidKind.Roof).Max(s => s.Number));
    }

    [Fact]
    public void Each_number_is_scored_on_its_piece_clear_of_the_texture_where_there_is_room()
    {
        var (_, kit) = Sample.Value;
        int marked = 0, clear = 0;
        foreach (var p in kit.Pieces)
        {
            if (p.Marks.Count == 0) continue;
            marked++;
            // Every stroke on the piece.
            var inside = Geo.ClipLines(p.Marks, Geo.Offset(p.Cut, 0.01));
            Assert.Equal(Length(p.Marks), Length(inside), 1);
            // Clear of what's etched on it (a plain patch is left for it on a piece etched all over).
            // (it may graze the edge of a groove: the spot is found on a grid a fifth of a millimetre or so across.)
            if (p.Etched.Count == 0 || Length(Geo.ClipLines(p.Marks, Geo.Union(p.Etched))) < 0.05 * Length(p.Marks)) clear++;
            else Assert.Fail($"{p.Name}'s number runs over its texture: {Length(Geo.ClipLines(p.Marks, Geo.Union(p.Etched))):0.##} of {Length(p.Marks):0.##} mm");
        }
        // Thin pieces (window frames, linings, legs, the furniture's small glued-on carvings and handles) have no room
        // for even the smallest number, and three-figure numbers need more room still.
        Assert.True(marked >= kit.Pieces.Count * 0.7, $"only {marked} of {kit.Pieces.Count} pieces are marked");
        Assert.All(kit.Pieces.Where(p => p.Marks.Count == 0), p => Assert.True(Narrow(p.Cut), $"{p.Name} is big enough for its number"));
        Assert.Equal(marked, clear);
    }

    [Fact]
    public void Numbers_read_the_right_way_up_and_six_and_nine_are_underlined()
    {
        var six = HouseNumbers.Write("6", 0, 0, 3);
        var sixteen = HouseNumbers.Write("16", 0, 0, 3);
        var twelve = HouseNumbers.Write("12", 0, 0, 3);
        // A stroke a digit (an 8 is two loops), and an underline under the ones that turn into another number.
        Assert.Equal(2, six.Count);
        Assert.Equal(2, HouseNumbers.Write("9", 0, 0, 3).Count);
        Assert.Equal(2, HouseNumbers.Write("8", 0, 0, 3).Count);
        Assert.Equal(3, sixteen.Count);
        Assert.Equal(2, twelve.Count);
        Assert.Equal(2, HouseNumbers.Write("69", 0, 0, 3).Count);
        var line = sixteen[^1];
        Assert.True(line.Count == 2 && Math.Abs(line[0].y - line[1].y) < 1e-9 && Math.Abs(line[0].y + HouseNumbers.Height("16", 3) / 2) < 1e-9);
        Assert.All(sixteen.SelectMany(l => l), p => Assert.True(Math.Abs(p.y) <= HouseNumbers.Height("16", 3) / 2 + 1e-6));
        // Every stroke within the box it's said to take (the underline just under it).
        double w = HouseNumbers.Width("12", 3);
        Assert.All(twelve.SelectMany(l => l), p => Assert.True(Math.Abs(p.x) <= w / 2 + 1e-6 && Math.Abs(p.y) <= 1.5 + 1e-6, $"{p.x}, {p.y}"));
    }

    [Fact]
    public void A_piece_etched_all_over_gets_a_plain_patch_for_its_number()
    {
        PathsD square = [Geo.Rect(0, 0, 40, 30)];
        var piece = new Piece("Floor", Sheet.Walls, square, square, [], 1) { Number = 7 };
        var marked = HouseNumbers.Mark(piece);
        Assert.NotEmpty(marked.Marks);
        Assert.True(Geo.Area(marked.Etched) < Geo.Area(square) - 10);
        Assert.Equal(0, Length(Geo.ClipLines(marked.Marks, Geo.Union(marked.Etched))), 3);
        // Low on the piece (near its bottom edge), in the middle across.
        var b = Geo.Bounds(new PathsD(marked.Marks));
        Assert.True(b.top < 8 && Math.Abs((b.left + b.right) / 2 - 20) < 1, $"{b.left}, {b.top} – {b.right}, {b.bottom}");
    }

    [Fact]
    public void The_steps_use_every_piece_cut_exactly()
    {
        var (_, kit) = Sample.Value;
        var steps = HouseSteps.Steps(kit);
        Assert.True(steps.Count >= 8, $"{steps.Count} steps");
        // Each piece in the model is put on in exactly one step.
        var used = steps.Where(s => !s.Alone).SelectMany(s => s.Solids).ToList();
        // A box wall is put together on its own, then stood up in the next step (which takes no new pieces).
        Assert.Contains(steps, s => s.Alone);
        for (int i = 0; i < steps.Count; i++)
            if (steps[i].Alone) Assert.Contains(steps.Skip(i + 1), s => s.PutsIn && s.Parts.Count == 0 && steps[i].Solids.All(s.Solids.Contains));
        Assert.Equal(kit.Solids.Count, used.Count);
        Assert.Equal(kit.Solids.Count, used.Distinct().Count());
        // The counts add up to the copies cut.
        var cut = kit.Pieces.GroupBy(p => p.Number).ToDictionary(g => g.Key, g => g.Sum(p => p.Copies));
        var taken = steps.SelectMany(s => s.Parts).GroupBy(x => x.Number).ToDictionary(g => g.Key, g => g.Sum(x => x.Count));
        foreach (var (n, copies) in cut) Assert.True(taken.GetValueOrDefault(n) == copies, $"#{n}: {copies} cut, {taken.GetValueOrDefault(n)} used");
        // From the ground up: no step on a floor before one of a floor below (the roofs come last).
        var levels = steps.Where(s => !s.Title.StartsWith("Roof", StringComparison.Ordinal)).Select(s => s.Level).ToList();
        Assert.Equal(levels.Order(), levels);
        Assert.StartsWith("Roof", steps[^1].Title);
        Assert.Contains(steps, s => s.Inside);
    }

    [Fact]
    public void The_booklet_is_saved_with_a_cover_the_pieces_and_a_page_a_step()
    {
        var (design, kit) = Sample.Value;
        var path = Path.Combine(Path.GetTempPath(), $"house-booklet-{Guid.NewGuid():N}.pdf");
        try
        {
            int pages = HouseBooklet.SavePdf(kit, design, new HouseBooklet.Options { Title = "Sample house" }, path);
            Assert.True(pages >= HouseSteps.Steps(kit).Count + 2, $"{pages} pages");
            var bytes = File.ReadAllBytes(path);
            Assert.True(bytes.Length > 20_000);
            Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        }
        finally { File.Delete(path); }
    }

    /// <summary>Nowhere on it is there room for a number with room round it (it's all thin strips).</summary>
    static bool Narrow(PathsD cut) => Geo.Area(Geo.Offset(cut, -2.2)) < 1;

    static double Length(IEnumerable<PathD> lines) =>
        lines.Sum(l => l.Zip(l.Skip(1), (a, b) => Math.Sqrt((a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y))).Sum());
}
