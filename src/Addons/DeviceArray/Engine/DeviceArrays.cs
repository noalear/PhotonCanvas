using System.Collections.Immutable;
using System.Security.Cryptography;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Addon.DeviceArray;

/// <summary>
/// Device arrays in a design. The shapes of the first device (the design) carry the array's notes:
/// <see cref="ArrayKey"/> (which array), <see cref="PartKey"/> (which of the design's shapes, kept by
/// its copies), <see cref="CellKey"/> ("0,0"), <see cref="SpecKey"/> (the matrix, <see cref="ArraySpec.Write"/>)
/// and, for a shape that was a guide (output off) when the array was made, <see cref="GuideKey"/>.
/// The other devices are ordinary shapes, copies of those, with the array, part and their own cell:
/// a device that's off has its copies' output off, and each copy's fields are numbered ahead
/// (<see cref="Shape.VarOffset"/>) so every device gets its own serial number or spreadsheet row.
/// Being plain shapes, they mark the same with the add-on off.
/// </summary>
public static class DeviceArrays
{
    public const string ArrayKey = "devicearray.array", PartKey = "devicearray.part", CellKey = "devicearray.cell",
        SpecKey = "devicearray.spec", GuideKey = "devicearray.guide";
    const string Prefix = "devicearray.";

    public static bool IsTagged(Shape s) => s.AddonData.Count > 0 && s.AddonData.ContainsKey(ArrayKey);

    public static Guid? ArrayOf(Shape s) => s.AddonData.TryGetValue(ArrayKey, out var v) && Guid.TryParse(v, out var g) ? g : null;
    public static Guid? PartOf(Shape s) => s.AddonData.TryGetValue(PartKey, out var v) && Guid.TryParse(v, out var g) ? g : null;
    public static Cell? CellOf(Shape s) => s.AddonData.TryGetValue(CellKey, out var v) ? Cell.Parse(v) : null;
    public static bool IsFirst(Shape s) => CellOf(s) is { IsFirst: true };
    public static bool IsGuide(Shape s) => s.AddonData.ContainsKey(GuideKey);

    /// <summary>One array as it is in the design.</summary>
    public sealed record ArrayView(Guid Id, ArraySpec Spec, IReadOnlyList<Shape> Firsts, int Copies)
    {
        /// <summary>Round the first device's shapes.</summary>
        public Rect2 FirstBounds => Firsts.Aggregate(Rect2.Empty, (r, s) => r.Union(s.Bounds));

        /// <summary>A device's place: the pitch's size, centred on where its copy of the design is.</summary>
        public Rect2 CellBox(Cell c)
        {
            var mid = FirstBounds.Center + Spec.Offset(c);
            double w = Math.Abs(Spec.PitchX) > 1e-9 ? Math.Abs(Spec.PitchX) : FirstBounds.Width;
            double h = Math.Abs(Spec.PitchY) > 1e-9 ? Math.Abs(Spec.PitchY) : FirstBounds.Height;
            return new Rect2(mid.X - w / 2, mid.Y - h / 2, mid.X + w / 2, mid.Y + h / 2);
        }

        /// <summary>Round every device.</summary>
        public Rect2 Bounds
        {
            get
            {
                var b = FirstBounds;
                var far = Spec.Offset(new Cell(Spec.Columns - 1, Spec.Rows - 1));
                return b.Union(new Rect2(b.MinX + far.X, b.MinY + far.Y, b.MaxX + far.X, b.MaxY + far.Y));
            }
        }
    }

    /// <summary>The arrays in the design (on this page), in the order their first shapes come.</summary>
    public static IEnumerable<Guid> Arrays(DocState s)
    {
        var seen = new HashSet<Guid>();
        foreach (var sh in s.Shapes)
            if (IsTagged(sh) && ArrayOf(sh) is { } id && seen.Add(id)) yield return id;
    }

    public static ArrayView? Find(DocState s, Guid id)
    {
        var firsts = new List<Shape>();
        int copies = 0;
        foreach (var sh in s.Shapes)
        {
            if (!IsTagged(sh) || ArrayOf(sh) != id) continue;
            if (IsFirst(sh)) firsts.Add(sh); else copies++;
        }
        if (firsts.Count == 0) return null;
        return new ArrayView(id, SpecOf(firsts), firsts, copies);
    }

    /// <summary>The array any of these shapes belongs to (the first found), if any.</summary>
    public static ArrayView? Touching(DocState s, IReadOnlySet<Guid> ids)
    {
        if (ids.Count == 0) return null;
        foreach (var sh in s.Shapes)
            if (ids.Contains(sh.Id) && IsTagged(sh) && ArrayOf(sh) is { } a && Find(s, a) is { } v) return v;
        return null;
    }

    /// <summary>The device a shape is in, if it's in an array.</summary>
    public static (Guid Array, Cell Cell)? DeviceOf(Shape s) => IsTagged(s) && ArrayOf(s) is { } a && CellOf(s) is { } c ? (a, c) : null;

    static ArraySpec SpecOf(IEnumerable<Shape> firsts)
    {
        foreach (var f in firsts)
            if (f.AddonData.TryGetValue(SpecKey, out var text)) return ArraySpec.Read(text);
        return new ArraySpec();
    }

    /// <summary>
    /// Make these shapes the first device of a new array and put the copies in. Shapes already in an
    /// array are left out. Shapes whose output is off stay guides (never marked) on every device.
    /// </summary>
    public static (DocState State, Guid Id) Create(DocState s, IReadOnlySet<Guid> ids, ArraySpec spec)
    {
        var id = Guid.NewGuid();
        var text = spec.Normalised().Write();
        var shapes = s.Shapes.Select(sh =>
        {
            if (!ids.Contains(sh.Id) || IsTagged(sh)) return sh;
            var data = sh.AddonData.SetItem(ArrayKey, id.ToString()).SetItem(PartKey, Guid.NewGuid().ToString())
                .SetItem(CellKey, new Cell(0, 0).ToString()).SetItem(SpecKey, text);
            if (!sh.Output) data = data.SetItem(GuideKey, "1");
            return sh.WithAddonData(data);
        }).ToImmutableList();
        var made = s with { Shapes = shapes };
        return (Sync(made, id), id);
    }

    /// <summary>Change the matrix (and so the copies).</summary>
    public static DocState SetSpec(DocState s, Guid id, ArraySpec spec)
    {
        var view = Find(s, id);
        if (view is null) return s;
        return Rebuild(s, id, view.Firsts, spec, Previous(view.Firsts));
    }

    /// <summary>Move the whole array (its first device, and so the copies).</summary>
    public static DocState Move(DocState s, Guid id, Vec2 by)
    {
        var view = Find(s, id);
        if (view is null || (by.X == 0 && by.Y == 0)) return s;
        var t = Affine.Translate(by);
        return Rebuild(s, id, [.. view.Firsts.Select(f => f.With(transform: f.Transform.Then(t)))], view.Spec, Previous(view.Firsts));
    }

    /// <summary>Put the copies back as the first device and the matrix say.</summary>
    public static DocState Sync(DocState s, Guid id) => Find(s, id) is { } v ? Rebuild(s, id, v.Firsts, v.Spec, Previous(v.Firsts)) : s;

    static Dictionary<Guid, Shape> Previous(IEnumerable<Shape> firsts) => firsts.ToDictionary(f => f.Id);

    /// <summary>Keep every device as plain shapes (no longer an array): they stay where they are, the ones that are off stay off.</summary>
    public static DocState Release(DocState s, Guid id) =>
        s with { Shapes = [.. s.Shapes.Select(sh => IsTagged(sh) && ArrayOf(sh) == id ? Untag(sh) : sh)] };

    /// <summary>Take the copies away, leaving the first device as it was before the array.</summary>
    public static DocState Remove(DocState s, Guid id)
    {
        var shapes = ImmutableList.CreateBuilder<Shape>();
        foreach (var sh in s.Shapes)
        {
            if (!IsTagged(sh) || ArrayOf(sh) != id) { shapes.Add(sh); continue; }
            if (!IsFirst(sh)) continue;
            shapes.Add(Untag(sh).WithOutput(!IsGuide(sh)).WithVarOffset(0));
        }
        return s with { Shapes = shapes.ToImmutable() };
    }

    /// <summary>The shape without the array's notes.</summary>
    public static Shape Untag(Shape sh) =>
        sh.WithAddonData(sh.AddonData.RemoveRange(sh.AddonData.Keys.Where(k => k.StartsWith(Prefix, StringComparison.Ordinal)).ToList()));

    /// <summary>
    /// The array made again from its first device (<paramref name="firsts"/>, in their new form) and
    /// <paramref name="spec"/>: the first device's shapes are replaced where they are, and the copies of
    /// every other device come where the old copies were (or after the first device). A copy keeps its
    /// id from before, so a selection of it stays. <paramref name="previous"/> (by id) is how the
    /// first device's shapes were when the copies there are now were made: a shape whose outline
    /// hasn't changed since (only moved, switched or renamed) keeps its copies' own outlines, which
    /// for text with a serial number saves laying out every copy's words again (dragging stays quick).
    /// </summary>
    public static DocState Rebuild(DocState s, Guid id, IReadOnlyList<Shape> firsts, ArraySpec spec, IReadOnlyDictionary<Guid, Shape>? previous = null)
    {
        spec = spec.Normalised();
        var numbers = spec.Numbers();
        string specText = spec.Write();
        bool firstOn = spec.IsOn(new Cell(0, 0));
        int firstNumber = numbers[new Cell(0, 0)];

        // The first device as it should be.
        var newFirsts = new Dictionary<Guid, Shape>(firsts.Count);
        var ordered = new List<Shape>(firsts.Count);
        foreach (var f in firsts)
        {
            bool guide = IsGuide(f);
            var nf = f;
            if (!f.AddonData.TryGetValue(SpecKey, out var had) || had != specText) nf = nf.WithAddonData(SpecKey, specText);
            if (nf.Output != (firstOn && !guide)) nf = nf.WithOutput(firstOn && !guide);
            if (nf.VarOffset != firstNumber) nf = nf.WithVarOffset(firstNumber);
            newFirsts[f.Id] = nf;
            ordered.Add(nf);
        }

        // The copies there are now, to keep their ids (and their outlines, when nothing but the place changed).
        var had2 = new Dictionary<(Guid, Cell), Shape>();
        foreach (var sh in s.Shapes)
            if (IsTagged(sh) && ArrayOf(sh) == id && !IsFirst(sh) && PartOf(sh) is { } p && CellOf(sh) is { } c)
                had2.TryAdd((p, c), sh);
        var sameOutline = ordered.Select(f => previous is not null && previous.TryGetValue(f.Id, out var was) && SameOutline(was, f)).ToList();
        var usedIds = new HashSet<Guid>(newFirsts.Values.Select(f => f.Id));

        // Every other device: a copy of each of the first device's shapes, moved by its offset.
        var copies = new List<Shape>(ordered.Count * Math.Max(0, spec.Count - 1));
        var baseData = ordered.Select(f => f.AddonData.Remove(SpecKey).Remove(GuideKey)).ToList();
        foreach (var cell in spec.Cells())
        {
            if (cell.IsFirst) continue;
            bool on = spec.IsOn(cell);
            var t = Affine.Translate(spec.Offset(cell));
            var cellText = cell.ToString();
            for (int i = 0; i < ordered.Count; i++)
            {
                var f = ordered[i];
                var part = PartOf(f) ?? f.Id;
                had2.TryGetValue((part, cell), out var old);
                var copyId = old is not null && usedIds.Add(old.Id) ? old.Id : Guid.NewGuid();
                bool reuse = old is not null && copyId == old.Id && sameOutline[i] && old.GetType() == f.GetType() && old.VarOffset == numbers[cell];
                var copy = (reuse ? old!.With(transform: f.Transform.Then(t), name: f.Name, layerId: f.LayerId).WithLocked(f.Locked).WithVisible(f.Visible)
                        : f.With(id: copyId, transform: f.Transform.Then(t)))
                    .WithGroup(f.GroupId is { } g ? CellGroup(g, cell) : null)
                    .WithVarOffset(numbers[cell])
                    .WithOutput(on && !IsGuide(f))
                    .WithAddonData(baseData[i].SetItem(CellKey, cellText));
                copies.Add(copy);
            }
        }

        // Put them in the design.
        var shapes = ImmutableList.CreateBuilder<Shape>();
        int insertAt = -1, lastFirst = -1;
        foreach (var sh in s.Shapes)
        {
            if (IsTagged(sh) && ArrayOf(sh) == id)
            {
                if (newFirsts.TryGetValue(sh.Id, out var nf)) { shapes.Add(nf); lastFirst = shapes.Count; continue; }
                if (insertAt < 0) insertAt = shapes.Count;
                continue; // an old copy, or a first-device shape that's gone from the design
            }
            shapes.Add(sh);
        }
        if (insertAt < 0) insertAt = lastFirst < 0 ? shapes.Count : lastFirst;
        shapes.InsertRange(insertAt, copies);
        var result = s with { Shapes = shapes.ToImmutable() };
        // Text and codes with fields show each device's own values straight away (also while dragging).
        return copies.Concat(ordered).Any(HasFields) ? VariableText.Refresh(result, DateTime.Now) : result;
    }

    /// <summary>
    /// Has <paramref name="now"/> the same outline as <paramref name="was"/>, so a copy made from that one
    /// only needs moving: the same geometry object (moving or switching a shape keeps it), tabs and start point.
    /// </summary>
    static bool SameOutline(Shape was, Shape now) =>
        ReferenceEquals(was, now)
        || was is PathShape a && now is PathShape b && a.GetType() == b.GetType() && ReferenceEquals(a.Geometry, b.Geometry)
           && ReferenceEquals(a.ManualTabs, b.ManualTabs) && a.StartPoint == b.StartPoint;

    static bool HasFields(Shape s) => s switch
    {
        TextShape t => VariableText.HasFields(t.Text),
        CodeShape c => VariableText.HasFields(c.Spec.Content),
        _ => false,
    };

    /// <summary>The group a device's copies of group <paramref name="g"/> go in: the same for that device every time.</summary>
    static Guid CellGroup(Guid g, Cell c)
    {
        Span<byte> bytes = stackalloc byte[24];
        g.TryWriteBytes(bytes);
        BitConverter.TryWriteBytes(bytes[16..], c.C);
        BitConverter.TryWriteBytes(bytes[20..], c.R);
        Span<byte> hash = stackalloc byte[16];
        MD5.HashData(bytes, hash);
        return new Guid(hash);
    }
}
