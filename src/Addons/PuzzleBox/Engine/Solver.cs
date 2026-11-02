namespace PhotonCanvas.Addon.PuzzleBox;

/// <summary>Which way a body is moved, as the person holding the box sees it (the front face towards them).</summary>
public enum Dir { Left, Right, Forward, Back, Up }

/// <summary>One move: a body pushed one way as far as it goes, or taken out of the box.</summary>
public sealed record Move(int Body, Dir Dir, double Distance, bool Out);

/// <summary>Where every body is: how far it has moved from where it sits when the box is shut, and whether it's out.</summary>
public sealed record Pose(double[] Dx, double[] Dy, bool[] Out)
{
    public string Key()
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < Dx.Length; i++)
            sb.Append(Out[i] ? "o" : $"{Math.Round(Dx[i], 2)}:{Math.Round(Dy[i], 2)}").Append('|');
        return sb.ToString();
    }

    public static Pose Shut(int n) => new(new double[n], new double[n], new bool[n]);

    public Pose With(int i, double dx, double dy, bool @out)
    {
        var x = (double[])Dx.Clone();
        var y = (double[])Dy.Clone();
        var o = (bool[])Out.Clone();
        x[i] = dx; y[i] = dy; o[i] = @out;
        return new(x, y, o);
    }
}

/// <summary>The shortest way in, the poses along it, and what that takes.</summary>
public sealed record Solution(bool Solved, List<Move> Moves, List<Pose> Poses, int States, bool TooMany)
{
    /// <summary>The move after which the box itself is open (−1: never), and after which the drawer is (−1: no drawer or never).</summary>
    public int BoxOpenAt { get; init; } = -1;
    public int DrawerOpenAt { get; init; } = -1;
}

/// <summary>
/// Plays the box: from shut, every body the person can get at is pushed each way as far as it goes
/// (moves shorter than <see cref="MinMove"/> are play, not moves) or lifted off, breadth first, until
/// the box is open and, if there is one, the drawer too. Bodies only bump into bodies in the same
/// layer, and everything is made of rectangles on the plan, so each push is worked out exactly.
/// </summary>
public static class Solver
{
    /// <summary>Further than this counts as out of the box.</summary>
    public const double Away = 1000;
    public const int MaxStates = 60000;

    /// <summary>How far is a move rather than play in the fits.</summary>
    public static double MinMove(double fit) => Math.Max(1.0, 3 * fit);

    /// <summary>
    /// How far body <paramref name="b"/> can go along <paramref name="dir"/> (in-plane) before it touches
    /// something, from <paramref name="p"/>; <see cref="Away"/> or more if nothing stops it. Negative if it already overlaps something.
    /// </summary>
    public static double Travel(BoxDesign d, Pose p, int b, Dir dir)
    {
        const double eps = 1e-6;
        double best = double.MaxValue;
        var body = d.Bodies[b];
        foreach (var (layer, rects) in body.Solid)
        {
            for (int o = 0; o < d.Bodies.Count; o++)
            {
                if (o == b || p.Out[o] || !d.Bodies[o].Solid.TryGetValue(layer, out var others)) continue;
                foreach (var ra in rects)
                {
                    var a = ra.Move(p.Dx[b], p.Dy[b]);
                    foreach (var rb in others)
                    {
                        var c = rb.Move(p.Dx[o], p.Dy[o]);
                        var (ox, oy) = a.Overlap(c);
                        if (ox > eps && oy > eps) return -1;
                        double t = dir switch
                        {
                            Dir.Right when oy > eps && c.X0 >= a.X1 - eps => c.X0 - a.X1,
                            Dir.Left when oy > eps && c.X1 <= a.X0 + eps => a.X0 - c.X1,
                            Dir.Back when ox > eps && c.Y0 >= a.Y1 - eps => c.Y0 - a.Y1,
                            Dir.Forward when ox > eps && c.Y1 <= a.Y0 + eps => a.Y0 - c.Y1,
                            _ => double.MaxValue,
                        };
                        if (t < best) best = Math.Max(0, t);
                    }
                }
            }
        }
        return best;
    }

    /// <summary>Whether body <paramref name="b"/> can be lifted straight up and off: nothing above any of its pieces.</summary>
    public static bool Lifts(BoxDesign d, Pose p, int b)
    {
        const double eps = 1e-6;
        var body = d.Bodies[b];
        foreach (var (layer, rects) in body.Solid)
            for (int o = 0; o < d.Bodies.Count; o++)
            {
                if (o == b || p.Out[o]) continue;
                foreach (var (l2, others) in d.Bodies[o].Solid)
                {
                    if (l2 <= layer) continue;
                    foreach (var ra in rects)
                    {
                        var a = ra.Move(p.Dx[b], p.Dy[b]);
                        foreach (var rb in others)
                        {
                            var (ox, oy) = a.Overlap(rb.Move(p.Dx[o], p.Dy[o]));
                            if (ox > eps && oy > eps) return false;
                        }
                    }
                }
            }
        return true;
    }

    /// <summary>Pairs of bodies whose pieces are in the same place (in the same layer) in a pose: none, in a box that goes together.</summary>
    public static List<string> Overlaps(BoxDesign d, Pose p)
    {
        var found = new List<string>();
        for (int a = 0; a < d.Bodies.Count; a++)
            for (int b = a + 1; b < d.Bodies.Count; b++)
            {
                if (p.Out[a] || p.Out[b]) continue;
                foreach (var (layer, ra) in d.Bodies[a].Solid)
                {
                    if (!d.Bodies[b].Solid.TryGetValue(layer, out var rb)) continue;
                    double worst = 0;
                    foreach (var x in ra)
                        foreach (var y in rb)
                        {
                            var (ox, oy) = x.Move(p.Dx[a], p.Dy[a]).Overlap(y.Move(p.Dx[b], p.Dy[b]));
                            if (ox > 1e-6 && oy > 1e-6) worst = Math.Max(worst, ox * oy);
                        }
                    if (worst > 0) found.Add($"{d.Bodies[a].Name} and {d.Bodies[b].Name} in layer {layer + 1} ({worst:0.##} mm²)");
                }
            }
        return found;
    }

    public static bool Reachable(BoxDesign d, Pose p, int b) => d.Bodies[b].Role != BodyRole.Frame && d.Bodies[b].ReachAfter.All(o => p.Out[o]);

    /// <summary>Is the box open (everything over the top out), and is the drawer open (out, or pulled far enough to get things out).</summary>
    public static bool BoxOpen(BoxDesign d, Pose p) => d.TopBodies.Count > 0 && d.TopBodies.All(i => p.Out[i]);
    public static bool DrawerOpen(BoxDesign d, Pose p) => d.DrawerBody is int i && (p.Out[i] || -p.Dy[i] >= d.DrawerOpensMm);

    /// <summary>Every move from a pose: (the move, the pose it leads to).</summary>
    public static IEnumerable<(Move Move, Pose Next)> Moves(BoxDesign d, Pose p)
    {
        double min = MinMove(d.Fit);
        for (int b = 0; b < d.Bodies.Count; b++)
        {
            if (p.Out[b] || !Reachable(d, p, b)) continue;
            foreach (var dir in new[] { Dir.Left, Dir.Right, Dir.Forward, Dir.Back })
            {
                double t = Travel(d, p, b, dir);
                if (t < min) continue;
                if (t >= Away) { yield return (new Move(b, dir, t, true), p.With(b, p.Dx[b], p.Dy[b], true)); continue; }
                double dx = dir == Dir.Right ? t : dir == Dir.Left ? -t : 0, dy = dir == Dir.Back ? t : dir == Dir.Forward ? -t : 0;
                yield return (new Move(b, dir, t, false), p.With(b, p.Dx[b] + dx, p.Dy[b] + dy, false));
            }
            if (Lifts(d, p, b)) yield return (new Move(b, Dir.Up, Away, true), p.With(b, p.Dx[b], p.Dy[b], true));
        }
    }

    /// <summary>The shortest way to open everything: the box, then the drawer if there is one (or just the drawer, for a closed block).</summary>
    public static Solution Solve(BoxDesign d, int maxStates = MaxStates)
    {
        var start = Pose.Shut(d.Bodies.Count);
        bool Done(Pose p) => (d.TopBodies.Count == 0 || BoxOpen(d, p)) && (d.DrawerBody is null || DrawerOpen(d, p));
        var seen = new Dictionary<string, (string? From, Move? Move, Pose Pose)> { [start.Key()] = (null, null, start) };
        var queue = new Queue<Pose>([start]);
        string? goal = Done(start) ? start.Key() : null;
        bool tooMany = false;
        while (goal is null && queue.Count > 0)
        {
            var p = queue.Dequeue();
            string pk = p.Key();
            foreach (var (m, next) in Moves(d, p))
            {
                string k = next.Key();
                if (seen.ContainsKey(k)) continue;
                seen[k] = (pk, m, next);
                if (Done(next)) { goal = k; break; }
                queue.Enqueue(next);
            }
            if (seen.Count > maxStates) { tooMany = true; break; }
        }
        if (goal is null) return new(false, [], [start], seen.Count, tooMany);
        var moves = new List<Move>();
        var poses = new List<Pose>();
        for (string? k = goal; k is not null; k = seen[k].From)
        {
            var (from, move, pose) = seen[k];
            poses.Add(pose);
            if (move is not null) moves.Add(move);
        }
        moves.Reverse();
        poses.Reverse();
        int boxAt = d.TopBodies.Count == 0 ? -1 : poses.FindIndex(q => BoxOpen(d, q));
        int drawerAt = d.DrawerBody is null ? -1 : poses.FindIndex(q => DrawerOpen(d, q));
        return new(true, moves, poses, seen.Count, false) { BoxOpenAt = boxAt, DrawerOpenAt = drawerAt };
    }

    /// <summary>A move in words: "Push panel 2 to the right".</summary>
    public static string Words(BoxDesign d, Move m)
    {
        var b = d.Bodies[m.Body];
        string name = b.Role switch
        {
            BodyRole.Panel => $"panel {b.Number}",
            BodyRole.Lid => "the lid",
            BodyRole.Drawer => "the drawer",
            BodyRole.Button => "the button on the floor inside",
            _ => b.Name.ToLowerInvariant(),
        };
        string side = m.Dir switch { Dir.Left => "left", Dir.Right => "right", Dir.Back => "back", _ => "forward" };
        return (b.Role, m.Out) switch
        {
            (BodyRole.Lid, true) => "Lift the lid off.",
            (BodyRole.Drawer, true) => d.Spec.FingerNotch ? "Pull the drawer out (hook a fingertip in the notch underneath)." : "Pull the drawer out (tip the box forward).",
            (BodyRole.Panel, true) => $"Slide panel {b.Number} out of the front.",
            (_, true) => $"Take {name} out.",
            (BodyRole.Panel, false) => $"Push panel {b.Number} to the {side} ({m.Distance:0.#} mm).",
            (BodyRole.Button, false) => $"Slide the button on the floor inside to the {side} ({m.Distance:0.#} mm).",
            _ => $"Move {name} {side} ({m.Distance:0.#} mm).",
        };
    }
}
