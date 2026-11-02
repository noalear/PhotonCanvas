using Clipper2Lib;

namespace PhotonCanvas.Addon.PuzzleBox;

/// <summary>One piece to cut (flat, mm, y up = towards the back of the box): its outline and holes, what's etched on its top face, and how many like it one box has.</summary>
public sealed record Part(string Name, PathsD Cut, PathsD Etched, int Copies, BodyRole Role);

/// <summary>Every piece of a box, ready to cut.</summary>
public sealed record BoxKit(BoxDesign Design, List<Part> Parts)
{
    public bool Ok => Design.Ok && Parts.Count > 0;
    public int PieceCount => Parts.Sum(p => p.Copies) * Math.Max(1, Design.Spec.Count);
}

public static class Kit
{
    /// <summary>The pieces: one for each layer of each body, the same ones counted together.</summary>
    public static BoxKit Make(BoxDesign d)
    {
        var parts = new List<Part>();
        if (!d.Ok) return new(d, parts);
        var letters = BoxMaker.FrameLetters(d);
        foreach (var body in d.Bodies)
        {
            var groups = new List<(string Key, int Layer, int Count)>();
            foreach (var layer in body.Solid.Keys)
            {
                string key = BoxMaker.ShapeKey(body, layer) + "#" + EtchKey(body, layer);
                int at = groups.FindIndex(x => x.Key == key);
                if (at >= 0) groups[at] = groups[at] with { Count = groups[at].Count + 1 };
                else groups.Add((key, layer, 1));
            }
            foreach (var (_, layer, count) in groups)
            {
                var cut = body.Outline(layer);
                if (cut.Count == 0) continue;
                var etched = body.Etched.TryGetValue(layer, out var e) ? Geo.Intersect(Geo.Union(e), cut) : [];
                string name = body.PieceName(layer) + (body.Role == BodyRole.Frame && letters.TryGetValue(layer, out var l) ? $" ({l})" : "");
                var outers = Geo.Tidy(cut).Where(p => Clipper.Area(p) > 1e-6).ToList();
                if (outers.Count <= 1) { parts.Add(new Part(name, cut, etched, count, body.Role)); continue; }
                for (int i = 0; i < outers.Count; i++)
                {
                    var bit = Geo.Intersect(cut, [outers[i]]);
                    parts.Add(new Part($"{name} {(char)('a' + i)}", bit, Geo.Intersect(etched, bit), count, body.Role));
                }
            }
        }
        return new(d, parts);
    }

    static string EtchKey(Body b, int layer) =>
        b.Etched.TryGetValue(layer, out var e) ? $"{e.Count}:{e.Sum(p => p.Count)}:{e.Sum(p => p.Sum(q => q.x * 1.7 + q.y)):0.##}" : "";

    /// <summary>The box's layers from the bottom, by letter: "A, B ×5, C".</summary>
    public static string Order(BoxDesign d)
    {
        var letters = BoxMaker.FrameLetters(d);
        var runs = new List<(string L, int N)>();
        foreach (var layer in d.Bodies[0].Solid.Keys)
        {
            string l = letters[layer];
            if (runs.Count > 0 && runs[^1].L == l) runs[^1] = (l, runs[^1].N + 1);
            else runs.Add((l, 1));
        }
        return string.Join(", ", runs.Select(r => r.N > 1 ? $"{r.L} ×{r.N}" : r.L));
    }

    /// <summary>How it goes together, in words.</summary>
    public static List<string> Assembly(BoxDesign d)
    {
        var s = d.Spec;
        var steps = new List<string>();
        bool button = d.Bodies.Any(b => b.Role == BodyRole.Button);
        string marks = s.MarkPieces ? " (the letters go at the back left, face up)" : "";
        steps.Add($"Glue the box's layers in a stack, square at the back left corner{marks}: {Order(d)}.");
        if (d.DrawerBody is not null)
        {
            steps.Add($"Glue the drawer's layers the same way (D1 at the bottom), checking it slides in its opening before the glue sets.");
            if (button)
                steps.Add("Before the floor goes on: put the bolt in the floor's slot from above, glue the tooth under it (from below), and the button and its knob on top. Put the drawer in, then glue the floor on with the tooth hanging into the drawer at the back.");
        }
        if (s.Top == TopKind.SlidingPanels)
        {
            int k = d.Bodies.Count(b => b.Role == BodyRole.Panel);
            steps.Add(k > 1
                ? "Glue each panel's top onto its runner (the number underneath), lined up at the front and the left. Slide the panels in from the front, the back one first, each one shifted the way it came out (the opening steps, backwards), then push them all to the left: now they're locked."
                : "Glue the lid's top onto its runner, lined up at the front and the left, and slide it in from the front.");
        }
        else if (s.Top == TopKind.LiftLid)
            steps.Add("Glue the lid's lip under the lid, the same distance in from every edge.");
        return steps;
    }
}
