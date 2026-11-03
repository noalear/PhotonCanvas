using PhotonCanvas.Addon.Buildings;

namespace PhotonCanvas.Addon.HouseEditor;

/// <summary>One step of putting a building together: the pieces it adds (in the 3D model) and how many of each numbered piece that takes.</summary>
public sealed record BuildStep(string Title, int Level, List<int> Solids, List<(int Number, int Count)> Parts, string Tip)
{
    /// <summary>It goes inside the rooms (furniture, stairs): the pictures leave out the walls in front, to see in.</summary>
    public bool Inside { get; init; }
}

/// <summary>
/// The order a building goes together in, floor by floor from the ground up: the floor tiles and base, then
/// on each floor its floor, railings, columns, walls (a few at a time, their doors with them), ways up and
/// furniture; the roofs last.
/// </summary>
public static class HouseSteps
{
    /// <summary>The most walls put up in one step, and the most pieces of furniture.</summary>
    const int WallsPerStep = 4, ItemsPerStep = 5;
    /// <summary>The most different pieces a step of walls takes, unless one wall takes more.</summary>
    const int PartsPerStep = 12;

    enum Stage { Tiles, Base, Floor, Railings, Columns, Walls, WaysUp, Furniture, Roofs }

    static Stage StageOf(HouseSolid s) => s.Kind switch
    {
        SolidKind.Ground => s.Name.StartsWith("Floor tile", StringComparison.Ordinal) ? Stage.Tiles : Stage.Base,
        SolidKind.Floor => Stage.Floor,
        SolidKind.Railing => Stage.Railings,
        SolidKind.Column => Stage.Columns,
        SolidKind.Wall => Stage.Walls,
        SolidKind.Roof => Stage.Roofs,
        // A door goes up with its wall.
        _ when WallOf(s.Name) is not null => Stage.Walls,
        _ when IsWayUp(s.Name) => Stage.WaysUp,
        _ => Stage.Furniture,
    };

    static bool IsWayUp(string name) => name is "Stairs" or "Solid stairs" or "Spiral stairs" or "Ladder";

    /// <summary>The wall a piece belongs to ("A" for "Wall A front"), or null.</summary>
    static string? WallOf(string name)
    {
        if (!name.StartsWith("Wall ", StringComparison.Ordinal)) return null;
        int end = name.IndexOf(' ', 5);
        return end < 0 ? name[5..] : name[5..end];
    }

    /// <summary>When each piece of the model goes on: a number that rises step by step (the roofs after everything else).</summary>
    public static double[] Stages(IReadOnlyList<HouseSolid> solids) =>
        [.. solids.Select(s => StageOf(s) == Stage.Roofs ? 1000 + s.Level : StageOf(s) is Stage.Tiles or Stage.Base ? (int)StageOf(s) : s.Level * 10 + (int)StageOf(s))];

    /// <summary>The steps, in order, with the numbered pieces each one takes.</summary>
    public static List<BuildStep> Steps(HouseKit kit)
    {
        var solids = kit.Solids;
        int levels = solids.Count == 0 ? 0 : solids.Max(s => s.Level) + 1;
        string Floor(int k) => levels <= 1 ? "" : $"Floor {k + 1}: ";
        var stages = Stages(solids);
        var steps = new List<(string Title, int Level, List<int> Solids, string Tip)>();
        foreach (var group in Enumerable.Range(0, solids.Count).GroupBy(i => stages[i]).OrderBy(g => g.Key))
        {
            var first = solids[group.First()];
            var stage = StageOf(first);
            int k = first.Level;
            var all = group.ToList();
            switch (stage)
            {
                case Stage.Tiles:
                    steps.Add(("Floor tiles", 0, all, "Lock the floor tiles together by their jigsaw edges, etched side up."));
                    break;
                case Stage.Base:
                    steps.Add((all.Count > 1 ? "Base" : "Base plate", 0, all, all.Select(i => solids[i].Name).Distinct().Count() > 1
                        ? "Lay the base pieces side by side, etched side up: their edges meet where walls stand on them."
                        : "Lay the base plate etched side up: the lines scored on it show where the walls and columns stand."));
                    break;
                case Stage.Floor when k == 0:
                    steps.Add(("Floor 1: room floors", 0, all, "Drop each room's floor in between its walls and columns."));
                    break;
                case Stage.Floor:
                    steps.Add(($"Floor {k + 1}", k, all, all.Select(i => solids[i].Name).Distinct().Count() > 1
                        ? $"Lay floor {k + 1}'s pieces on top of the walls and columns below: their edges meet over walls."
                        : $"Lay floor {k + 1} on top of the walls and columns below."));
                    break;
                case Stage.Railings:
                    steps.Add(($"{Floor(k)}railings", k, all, "Push the railings' tabs into the slots along the floor's open edges and glue them."));
                    break;
                case Stage.Columns:
                    steps.Add(($"{Floor(k)}columns", k, all, "Put each column together and stand it at its corner or joint: the plate under it and the plan show where."));
                    break;
                case Stage.Walls:
                {
                    var byWall = all.GroupBy(i => WallOf(solids[i].Name) ?? "").OrderBy(g => g.Key.Length).ThenBy(g => g.Key, StringComparer.Ordinal).ToList();
                    // A few walls a step: as many as take no more than a dozen different pieces (a box wall alone can take ten).
                    for (int from = 0; from < byWall.Count;)
                    {
                        var chunk = new List<IGrouping<string, int>> { byWall[from++] };
                        while (from < byWall.Count && chunk.Count < WallsPerStep
                               && chunk.Append(byWall[from]).SelectMany(g => g).Select(i => solids[i].Number).Distinct().Count() <= PartsPerStep)
                            chunk.Add(byWall[from++]);
                        string letters = chunk.Count == 1 ? $"wall {chunk[0].Key}" : chunk.Count == 2 ? $"walls {chunk[0].Key} and {chunk[1].Key}" : $"walls {chunk[0].Key} to {chunk[^1].Key}";
                        steps.Add(($"{Floor(k)}{letters}", k, [.. chunk.SelectMany(g => g)],
                            chunk.Any(g => g.Any(i => solids[i].Kind == SolidKind.Furniture))
                                ? "Slide each wall's ends into the columns either side, then hang its door."
                                : "Slide each wall's ends into the columns either side."));
                    }
                    break;
                }
                case Stage.WaysUp:
                    steps.Add(($"{Floor(k)}{(all.Select(i => solids[i].Name).Distinct().Count() == 1 && solids[all[0]].Name == "Ladder" ? "ladder" : "ways up")}", k, all,
                        "Put the stairs (or ladder) together and stand them where shown: they go up through the opening in the floor above."));
                    break;
                case Stage.Furniture:
                {
                    var items = all.GroupBy(i => solids[i].Name).ToList();
                    for (int from = 0; from < items.Count; from += ItemsPerStep)
                        steps.Add(($"{Floor(k)}furniture", k, [.. items.Skip(from).Take(ItemsPerStep).SelectMany(g => g)],
                            "Put each piece of furniture together and stand it in its room."));
                    break;
                }
                case Stage.Roofs:
                    steps.Add((levels <= 1 ? "Roof" : $"Roofs on floor {k + 1}", k, all, "Put each roof together on its base and set it on top: it lifts off to see inside."));
                    break;
            }
        }

        // How many of each numbered piece each step takes: a piece's copies shared out over the places it's used
        // (a column's layers go with the column), and pieces not in the model (clips, pegs) with the piece numbered before them.
        var copies = new Dictionary<int, int>();
        foreach (var p in kit.Pieces.Where(p => p.Number > 0)) copies[p.Number] = copies.GetValueOrDefault(p.Number) + p.Copies;
        var placed = new Dictionary<int, int>();
        foreach (var s in solids.Where(s => s.Number > 0)) placed[s.Number] = placed.GetValueOrDefault(s.Number) + 1;
        var given = new Dictionary<int, int>();
        var seen = new Dictionary<int, int>();
        var result = new List<BuildStep>();
        var firstStepOf = new Dictionary<int, int>();
        foreach (var (title, level, list, tip) in steps)
        {
            var parts = new List<(int Number, int Count)>();
            foreach (var n in list.Select(i => solids[i].Number).Where(n => n > 0).Distinct().OrderBy(n => n))
            {
                int here = list.Count(i => solids[i].Number == n);
                seen[n] = seen.GetValueOrDefault(n) + here;
                // Rounded so that, step by step, the counts add up to the copies made.
                int upTo = (int)Math.Round((double)copies.GetValueOrDefault(n, here) * seen[n] / placed[n]);
                int count = upTo - given.GetValueOrDefault(n);
                given[n] = upTo;
                if (count > 0) parts.Add((n, count));
                firstStepOf.TryAdd(n, result.Count);
            }
            result.Add(new BuildStep(title, level, list, parts, tip) { Inside = list.All(i => StageOf(solids[i]) is Stage.WaysUp or Stage.Furniture) });
        }
        foreach (var n in copies.Keys.Where(n => !placed.ContainsKey(n)).OrderBy(n => n))
        {
            int before = firstStepOf.Keys.Where(m => m < n).DefaultIfEmpty(0).Max();
            int at = firstStepOf.TryGetValue(before, out int st) ? st : 0;
            if (result.Count == 0) break;
            result[at].Parts.Add((n, copies[n]));
            result[at].Parts.Sort((a, b) => a.Number.CompareTo(b.Number));
            firstStepOf.TryAdd(n, at);
        }
        return result;
    }
}
