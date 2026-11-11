using System.Collections.Immutable;
using System.Globalization;
using PhotonCanvas.Core;

namespace PhotonCanvas.Addon.DeviceArray;

/// <summary>The order the devices are counted in: which one gets the first serial number, which the next.</summary>
public enum CountOrder
{
    /// <summary>Row by row, left to right in each.</summary>
    Across,
    /// <summary>Column by column, top to bottom in each.</summary>
    Down,
    /// <summary>Row by row, turning back at the end of each (a snake).</summary>
    AcrossBackAndForth,
    /// <summary>Column by column, turning back at the end of each.</summary>
    DownBackAndForth,
}

/// <summary>One place in the matrix: column <see cref="C"/> and row <see cref="R"/>, from 0 at the first device (top left).</summary>
public readonly record struct Cell(int C, int R)
{
    public bool IsFirst => C == 0 && R == 0;
    public override string ToString() => $"{C},{R}";

    public static Cell? Parse(string? text)
    {
        if (text is null) return null;
        int comma = text.IndexOf(',');
        return comma > 0
            && int.TryParse(text.AsSpan(0, comma), NumberStyles.Integer, CultureInfo.InvariantCulture, out int c)
            && int.TryParse(text.AsSpan(comma + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int r)
            && c >= 0 && r >= 0 ? new Cell(c, r) : null;
    }
}

/// <summary>
/// The matrix: how many devices across and down, how far apart their centres are, the order they're
/// counted in, and which are off. The first device (top left) is the design itself; the others are copies of it.
/// </summary>
public sealed record ArraySpec
{
    /// <summary>Most columns or rows, and most devices in all (each is a copy of the whole design).</summary>
    public const int MaxSide = 200, MaxCells = 2500;

    public int Columns { get; init; } = 3;
    public int Rows { get; init; } = 2;
    /// <summary>Centre to centre across (mm).</summary>
    public double PitchX { get; init; } = 20;
    /// <summary>Centre to centre down (mm).</summary>
    public double PitchY { get; init; } = 20;
    public CountOrder Order { get; init; }
    /// <summary>
    /// A device that's off keeps its number (the serials follow the places in the tray); otherwise the
    /// devices that are on are numbered one after another and nothing is used up by the ones that are off.
    /// </summary>
    public bool KeepNumbers { get; init; }
    /// <summary>The devices that are off (not marked).</summary>
    public ImmutableHashSet<Cell> Off { get; init; } = [];

    public int Count => Columns * Rows;
    public bool Contains(Cell c) => c.C >= 0 && c.R >= 0 && c.C < Columns && c.R < Rows;
    public bool IsOn(Cell c) => !Off.Contains(c);
    public int OnCount => Count - Off.Count(Contains);

    /// <summary>How far a device is from the first one.</summary>
    public Vec2 Offset(Cell c) => new(c.C * PitchX, c.R * PitchY);

    /// <summary>Where a device comes in the counting order (0 = first).</summary>
    public int Rank(Cell c) => Order switch
    {
        CountOrder.Down => c.C * Rows + c.R,
        CountOrder.AcrossBackAndForth => c.R * Columns + (c.R % 2 == 0 ? c.C : Columns - 1 - c.C),
        CountOrder.DownBackAndForth => c.C * Rows + (c.C % 2 == 0 ? c.R : Rows - 1 - c.R),
        _ => c.R * Columns + c.C,
    };

    /// <summary>Every device, in counting order.</summary>
    public IEnumerable<Cell> Cells()
    {
        var all = new Cell[Count];
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Columns; c++)
            {
                var cell = new Cell(c, r);
                all[Rank(cell)] = cell;
            }
        return all;
    }

    /// <summary>
    /// How many steps ahead each device's fields are (its serial = the design's serial + n × step): its
    /// place in the order, or with <see cref="KeepNumbers"/> off, its place among the devices that are on
    /// (and 0 for those that are off, which aren't marked).
    /// </summary>
    public Dictionary<Cell, int> Numbers()
    {
        var n = new Dictionary<Cell, int>(Count);
        int next = 0, rank = 0;
        foreach (var c in Cells())
        {
            n[c] = KeepNumbers ? rank : IsOn(c) ? next++ : 0;
            rank++;
        }
        return n;
    }

    /// <summary>Within the limits, with no off devices outside the matrix.</summary>
    public ArraySpec Normalised()
    {
        int cols = Math.Clamp(Columns, 1, MaxSide), rows = Math.Clamp(Rows, 1, MaxSide);
        if (cols * rows > MaxCells) rows = Math.Max(1, MaxCells / cols);
        double px = double.IsFinite(PitchX) ? PitchX : 0, py = double.IsFinite(PitchY) ? PitchY : 0;
        var s = this with { Columns = cols, Rows = rows, PitchX = px, PitchY = py };
        return s.Off.All(s.Contains) ? s : s with { Off = [.. s.Off.Where(s.Contains)] };
    }

    /// <summary>Turn one device on or off.</summary>
    public ArraySpec With(Cell c, bool on) => on ? (Off.Contains(c) ? this with { Off = Off.Remove(c) } : this) : (Off.Contains(c) || !Contains(c) ? this : this with { Off = Off.Add(c) });

    /// <summary>As kept on the first device's shapes: "cols=3;rows=2;px=20;py=20;order=Across;keep=0;off=1,0 2,1".</summary>
    public string Write()
    {
        var off = string.Join(' ', Off.OrderBy(c => c.R).ThenBy(c => c.C));
        return string.Create(CultureInfo.InvariantCulture,
            $"cols={Columns};rows={Rows};px={PitchX:0.####};py={PitchY:0.####};order={Order};keep={(KeepNumbers ? 1 : 0)};off={off}");
    }

    /// <summary>Read what <see cref="Write"/> wrote; anything missing or unreadable keeps its default.</summary>
    public static ArraySpec Read(string? text)
    {
        var s = new ArraySpec();
        if (string.IsNullOrEmpty(text)) return s;
        foreach (var part in text.Split(';'))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0) continue;
            var key = part[..eq].Trim();
            var value = part[(eq + 1)..].Trim();
            var inv = CultureInfo.InvariantCulture;
            switch (key)
            {
                case "cols" when int.TryParse(value, NumberStyles.Integer, inv, out int c): s = s with { Columns = c }; break;
                case "rows" when int.TryParse(value, NumberStyles.Integer, inv, out int r): s = s with { Rows = r }; break;
                case "px" when double.TryParse(value, NumberStyles.Float, inv, out double px): s = s with { PitchX = px }; break;
                case "py" when double.TryParse(value, NumberStyles.Float, inv, out double py): s = s with { PitchY = py }; break;
                case "order" when Enum.TryParse<CountOrder>(value, out var o) && Enum.IsDefined(o): s = s with { Order = o }; break;
                case "keep": s = s with { KeepNumbers = value == "1" }; break;
                case "off":
                    s = s with { Off = [.. value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Cell.Parse).OfType<Cell>()] };
                    break;
            }
        }
        return s.Normalised();
    }
}
