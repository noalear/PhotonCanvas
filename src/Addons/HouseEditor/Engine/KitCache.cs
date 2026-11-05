using System.Collections.Concurrent;
using Clipper2Lib;
using Geo = PhotonCanvas.Addon.Walls.Geo;

namespace PhotonCanvas.Addon.HouseEditor;

/// <summary>
/// What the building editor made last time, kept so the next change doesn't make it all again: a wall,
/// the floor tiles, a floor's pattern, cut to its shape. Most changes touch one wall or one room, so
/// the rest comes straight from here and the 3D view keeps up.
/// <para>
/// Everything is found by what it was made from (its settings, or the shapes themselves), so a change
/// to any of that makes it again. What comes back is shared: don't change it in place.
/// </para>
/// </summary>
public static class KitCache
{
    /// <summary>
    /// How much is kept, in points of the shapes kept (about 16 bytes each), and the most things; past either,
    /// the oldest go first.
    /// </summary>
    const long MostPoints = 6_000_000, MostThings = 6000;

    sealed class Entry(Func<object> make)
    {
        public readonly Lazy<object> Value = new(make, LazyThreadSafetyMode.ExecutionAndPublication);
        public long Points;
    }

    static readonly ConcurrentDictionary<string, Entry> Made = new(StringComparer.Ordinal);
    static readonly ConcurrentQueue<(string Key, Entry Entry)> Order = new();
    static long _points;

    /// <summary>The thing made from <paramref name="key"/>: the one made before, or made now (once, however many ask at once).</summary>
    public static T Get<T>(string key, Func<T> make) where T : notnull
    {
        var mine = new Entry(() => make());
        var entry = Made.GetOrAdd(key, mine);
        object value;
        try { value = entry.Value.Value; }
        catch
        {
            // What failed is tried again next time.
            Made.TryRemove(new KeyValuePair<string, Entry>(key, entry));
            throw;
        }
        if (ReferenceEquals(entry, mine))
        {
            mine.Points = 1 + Points(value);
            Interlocked.Add(ref _points, mine.Points);
            Order.Enqueue((key, mine));
            while ((Interlocked.Read(ref _points) > MostPoints || Made.Count > MostThings) && Order.TryDequeue(out var old))
                if (Made.TryRemove(new KeyValuePair<string, Entry>(old.Key, old.Entry))) Interlocked.Add(ref _points, -old.Entry.Points);
        }
        return (T)value;
    }

    /// <summary>About how many points of shapes something holds.</summary>
    static long Points(object? o) => o switch
    {
        null => 0,
        PathD p => p.Count,
        IEnumerable<PathD> ps => ps.Sum(p => (long)p.Count),
        Walls.Part part => Points(part.Cut) + Points(part.Etched) + Points(part.Lines) + Points(part.BackEtched) + Points(part.BackLines),
        IEnumerable<Walls.Part> parts => parts.Sum(Points),
        Walls.WallKit kit => Points(kit.Parts),
        FloorTiles.TileKit kit => Points(kit.Parts),
        System.Runtime.CompilerServices.ITuple t => Enumerable.Range(0, t.Length).Sum(i => Points(t[i])),
        _ => 0,
    };

    /// <summary>Forgets everything.</summary>
    public static void Clear()
    {
        Made.Clear();
        Order.Clear();
        Interlocked.Exchange(ref _points, 0);
    }

    /// <summary>The areas of <paramref name="a"/> inside <paramref name="b"/> (as <see cref="Geo.Intersect"/>).</summary>
    public static PathsD Intersect(PathsD a, PathsD b) =>
        a.Count == 0 || b.Count == 0 ? [] : Get($"and|{Hash.Of(a)}|{Hash.Of(b)}", () => Geo.Intersect(a, b));

    /// <summary>The parts of <paramref name="lines"/> inside <paramref name="region"/> (as <see cref="Geo.ClipLines"/>).</summary>
    public static List<PathD> ClipLines(List<PathD> lines, PathsD region) =>
        lines.Count == 0 ? [] : Get($"clip|{Hash.Of(lines)}|{Hash.Of(region)}", () => Geo.ClipLines(lines, region));

    /// <summary>
    /// A fingerprint of shapes: every point's exact coordinates, run through two different 64-bit hashes, so two
    /// shapes that differ anywhere practically never come out the same.
    /// </summary>
    public sealed class Hash
    {
        ulong _a = 14695981039346656037UL, _b = 0x9E3779B97F4A7C15UL;

        public Hash Add(ulong v)
        {
            _a = (_a ^ v) * 1099511628211UL;
            _b = (_b ^ (v + 0x632BE59BD9B4E019UL + (_b << 6) + (_b >> 2))) * 0xBF58476D1CE4E5B9UL;
            return this;
        }

        public Hash Add(double v) => Add((ulong)BitConverter.DoubleToInt64Bits(v));

        public Hash Add(PathD p)
        {
            Add((ulong)p.Count);
            foreach (var q in p) { Add(q.x); Add(q.y); }
            return this;
        }

        public Hash Add(IEnumerable<PathD> paths)
        {
            ulong n = 0;
            foreach (var p in paths) { Add(p); n++; }
            return Add(n ^ 0xA5A5A5A5UL);
        }

        public override string ToString() => $"{_a:x16}{_b:x16}";

        public static string Of(IEnumerable<PathD> paths) => new Hash().Add(paths).ToString();
    }
}
