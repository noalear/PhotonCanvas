using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Extensions;

namespace PhotonCanvas.Addon.DeviceArray;

/// <summary>
/// Keeps every device array in step on each edit:
/// <list type="bullet">
/// <item>The first device's shapes changed (moved, resized, new words…): every copy follows.</item>
/// <item>A copy changed instead: that becomes the design, so all of them follow (drag any device to move the array).</item>
/// <item>A copy's or first shape's output (lightning) or a copy's eye switched: that device goes on or off, the others stay as they are.</item>
/// <item>A copy deleted: its device is turned off. A first-device shape deleted: its copies go too.</item>
/// <item>Copies pasted beside the array, with its first device: a second array; on their own: plain shapes.</item>
/// </list>
/// What changed is told apart by the shape objects: anything this rule made, or that was in the design
/// before the edit, is as it should be; a new object is what the person changed.
/// </summary>
public sealed class ArrayRule : IDocRule
{
    readonly ConditionalWeakTable<Shape, object> _known = new();
    static readonly object Mark = new();

    public string Name => "Device array";

    bool Known(Shape s) => _known.TryGetValue(s, out _);
    void Know(Shape s) => _known.AddOrUpdate(s, Mark);

    public DocState Apply(DocState before, DocState after)
    {
        if (!after.Shapes.Any(DeviceArrays.IsTagged)) return after;

        var beforeIds = new HashSet<Guid>(before.Shapes.Count);
        var beforeTagged = new Dictionary<Guid, Shape>();
        var beforeSlots = new Dictionary<Guid, HashSet<(Guid Part, Cell Cell)>>();
        foreach (var b in before.Shapes)
        {
            beforeIds.Add(b.Id);
            if (!DeviceArrays.IsTagged(b) || DeviceArrays.ArrayOf(b) is not { } a) continue;
            beforeTagged[b.Id] = b;
            Know(b);
            if (DeviceArrays.PartOf(b) is { } p && DeviceArrays.CellOf(b) is { } c)
            {
                if (!beforeSlots.TryGetValue(a, out var set)) beforeSlots[a] = set = [];
                set.Add((p, c));
            }
        }

        var state = Duplicates(after, beforeIds, beforeSlots);
        foreach (var id in DeviceArrays.Arrays(state).ToList())
            state = Keep(state, id, beforeTagged, beforeSlots.GetValueOrDefault(id));
        if (!ReferenceEquals(state, after))
            foreach (var sh in state.Shapes)
                if (DeviceArrays.IsTagged(sh)) Know(sh);
        return state;
    }

    /// <summary>
    /// Shapes new in this edit that take the place of an array's shape that's still there (pasted or
    /// duplicated): with a first-device shape among them they become a new array, otherwise plain shapes.
    /// </summary>
    static DocState Duplicates(DocState s, HashSet<Guid> beforeIds, Dictionary<Guid, HashSet<(Guid, Cell)>> beforeSlots)
    {
        if (beforeSlots.Count == 0) return s;
        var stayed = new HashSet<(Guid, Guid, Cell)>();
        List<Shape>? fresh = null;
        foreach (var sh in s.Shapes)
        {
            if (!DeviceArrays.IsTagged(sh) || DeviceArrays.ArrayOf(sh) is not { } a || !beforeSlots.ContainsKey(a)) continue;
            if (beforeIds.Contains(sh.Id))
            {
                if (DeviceArrays.PartOf(sh) is { } p && DeviceArrays.CellOf(sh) is { } c) stayed.Add((a, p, c));
            }
            else (fresh ??= []).Add(sh);
        }
        if (fresh is null) return s;
        var dups = fresh.Where(sh => DeviceArrays.PartOf(sh) is { } p && DeviceArrays.CellOf(sh) is { } c && stayed.Contains((DeviceArrays.ArrayOf(sh)!.Value, p, c))).ToList();
        if (dups.Count == 0) return s;

        var replace = new Dictionary<Guid, Shape>();
        foreach (var group in dups.GroupBy(sh => DeviceArrays.ArrayOf(sh)!.Value))
        {
            bool asArray = group.Any(DeviceArrays.IsFirst) && group.Any(sh => !DeviceArrays.IsFirst(sh));
            var newArray = Guid.NewGuid().ToString();
            var parts = new Dictionary<Guid, string>();
            foreach (var sh in group)
            {
                if (!asArray)
                {
                    var plain = DeviceArrays.Untag(sh);
                    replace[sh.Id] = DeviceArrays.IsFirst(sh) && DeviceArrays.IsGuide(sh) ? plain.WithOutput(false) : plain;
                    continue;
                }
                var part = DeviceArrays.PartOf(sh)!.Value;
                if (!parts.TryGetValue(part, out var np)) parts[part] = np = Guid.NewGuid().ToString();
                replace[sh.Id] = sh.WithAddonData(sh.AddonData.SetItem(DeviceArrays.ArrayKey, newArray).SetItem(DeviceArrays.PartKey, np));
            }
        }
        return s with { Shapes = [.. s.Shapes.Select(sh => replace.GetValueOrDefault(sh.Id, sh))] };
    }

    /// <summary>One array put back in step after the edit (the same state when nothing about it changed).</summary>
    DocState Keep(DocState s, Guid id, Dictionary<Guid, Shape> beforeTagged, HashSet<(Guid Part, Cell Cell)>? beforeSlots)
    {
        var firsts = new List<Shape>();
        var copies = new List<Shape>();
        foreach (var sh in s.Shapes)
        {
            if (!DeviceArrays.IsTagged(sh) || DeviceArrays.ArrayOf(sh) != id) continue;
            if (DeviceArrays.IsFirst(sh)) firsts.Add(sh); else copies.Add(sh);
        }
        bool existed = beforeSlots is not null;

        if (firsts.Count == 0)
        {
            // The first device was deleted: the array goes with it. Copies brought in without one are plain shapes.
            var gone = copies.Select(c => c.Id).ToHashSet();
            return existed
                ? s with { Shapes = s.Shapes.RemoveAll(sh => gone.Contains(sh.Id)) }
                : s with { Shapes = [.. s.Shapes.Select(sh => gone.Contains(sh.Id) ? DeviceArrays.Untag(sh) : sh)] };
        }

        var spec = DeviceArrays.Find(s, id)!.Spec;
        bool changed = !existed;
        var parts = new Dictionary<Guid, int>();
        for (int i = 0; i < firsts.Count; i++)
            if (DeviceArrays.PartOf(firsts[i]) is { } p) parts.TryAdd(p, i);

        // Shapes deleted: a first-device shape takes its copies with it (Rebuild leaves them out); a copy turns its device off.
        if (beforeSlots is not null)
        {
            var now = new HashSet<(Guid, Cell)>();
            foreach (var sh in firsts.Concat(copies))
                if (DeviceArrays.PartOf(sh) is { } p && DeviceArrays.CellOf(sh) is { } c) now.Add((p, c));
            foreach (var (part, cell) in beforeSlots)
            {
                if (now.Contains((part, cell))) continue;
                changed = true;
                if (!cell.IsFirst && parts.ContainsKey(part)) spec = spec.With(cell, false);
            }
        }

        // The first device's shapes the person changed.
        var edited = new bool[firsts.Count];
        for (int i = 0; i < firsts.Count; i++)
        {
            var f = firsts[i];
            if (Known(f)) continue;
            changed = true;
            edited[i] = true;
            if (!beforeTagged.TryGetValue(f.Id, out var b) || b.Output == f.Output) continue;
            if (DeviceArrays.IsGuide(f))
            {
                // A guide's output switched on: it's marked on every device from now on.
                if (f.Output) firsts[i] = f.WithAddonData(DeviceArrays.GuideKey, null);
            }
            else spec = spec.With(new Cell(0, 0), f.Output);
        }

        // Copies the person changed: switched on or off, or the design changed on one of them.
        var from = new Shape?[firsts.Count];
        foreach (var c in copies)
        {
            if (Known(c)) continue;
            changed = true;
            if (DeviceArrays.PartOf(c) is not { } part || !parts.TryGetValue(part, out int i) || DeviceArrays.CellOf(c) is not { } cell) continue;
            bool switched = false;
            if (beforeTagged.TryGetValue(c.Id, out var b))
            {
                if (b.Output != c.Output && !DeviceArrays.IsGuide(firsts[i])) { spec = spec.With(cell, c.Output); switched = true; }
                if (b.Visible != c.Visible) { spec = spec.With(cell, c.Visible); switched = true; }
            }
            if (!switched && !edited[i] && from[i] is null) from[i] = c;
        }
        if (!changed) return s;

        for (int i = 0; i < firsts.Count; i++)
        {
            if (from[i] is not { } c) continue;
            var f = firsts[i];
            var back = Affine.Translate(-spec.Offset(DeviceArrays.CellOf(c)!.Value));
            firsts[i] = c.With(id: f.Id, transform: c.Transform.Then(back)).WithGroup(f.GroupId).WithAddonData(f.AddonData)
                .WithVisible(f.Visible);
        }
        var previous = new Dictionary<Guid, Shape>();
        foreach (var f in firsts)
            if (beforeTagged.TryGetValue(f.Id, out var b) && DeviceArrays.IsFirst(b)) previous[f.Id] = b;
        return DeviceArrays.Rebuild(s, id, firsts, spec, previous);
    }
}
