using Clipper2Lib;
using PhotonCanvas.Addon.Buildings;
using PhotonCanvas.Addon.Furniture;
using PhotonCanvas.Addon.Walls;
using Pose = PhotonCanvas.Addon.Furniture.Pose;
using V3 = PhotonCanvas.Addon.Furniture.V3;
using Finding = PhotonCanvas.Addon.Buildings.Finding;
using Severity = PhotonCanvas.Addon.Buildings.Severity;

namespace PhotonCanvas.Addon.HouseEditor;

/// <summary>Doors that swing: in a flat wall, the furniture maker's door on two clips beside the doorway.</summary>
public static partial class HouseKitMaker
{
    /// <summary>Room a door's clips take beside its doorway, on the hinge side, clear of the column (mm).</summary>
    const double ClipRoomMm = 7;

    /// <summary>How far open the doors are shown in the 3D view, when they're shown open (degrees).</summary>
    public const double ShownOpenDeg = 75;

    /// <summary>A door that swings in a flat wall: where its doorway's left edge (seen from outside) is, on the wall's floor.</summary>
    internal sealed record SwungDoor(int Level, string Wall, Opening Door, V3 At, V3 U, V3 Facing, V3 Middle);

    /// <summary>The furniture maker's door for a doorway in a flat wall, hung on the side it's hinged.</summary>
    internal static FurnitureItem? ClipDoor(BuildingSpec s, Opening door) => (door.Shape switch
    {
        OpeningShape.Rectangle => DoorShape.Rectangle,
        OpeningShape.Pointed => DoorShape.Pointed,
        OpeningShape.Arched => DoorShape.Arched,
        _ => (DoorShape?)null,
    }) is not { } shape ? null : Defaults.For(ItemKind.WallDoor) with
    {
        WidthMm = door.WidthMm, HeightMm = door.HeightMm, WallMm = BuildingKitMaker.WallThickness(s), WallSheetMm = s.WallSheetMm,
        Shape = shape, OverlapMm = Math.Round(1.5 * K(s), 1), Count = 1,
        Hinge = door.Hinge == Walls.HingeSide.Right ? Furniture.HingeSide.Right : Furniture.HingeSide.Left,
    };

    /// <summary>
    /// Why a door in a flat wall can't swing on clips (null when it can): its doorway's shape, no room for the
    /// clips between it and the column on its hinge side, or a doorway too small for them.
    /// <paramref name="along"/> is the door's middle from the wall's start, <paramref name="length"/> the wall's (mm, column centre to centre).
    /// </summary>
    public static string? WhyNoSwing(BuildingSpec s, Opening door, double along, double length)
    {
        if (s.BoxWalls || door.Kind != OpeningKind.Door || !door.Opens) return null;
        if (ClipDoor(s, door) is not { } item) return "a door that swings is only made for square, arched and pointed doorways, so this one is glued in.";
        bool left = door.Hinge == Walls.HingeSide.Left;
        double room = left ? along - door.WidthMm / 2 - s.ColumnMm / 2 : length - along - door.WidthMm / 2 - s.ColumnMm / 2;
        if (room < ClipRoomMm)
            return $"there's no room for the door's clips beside it on the {(left ? "left" : "right")} ({room:0.#} mm to the column, it needs {ClipRoomMm:0.#}): hinge it on the other side or move it along. It's glued in for now.";
        var kit = FurnitureKit.Make(SetFor(s, [item]));
        if (kit.Items.Count == 0 || !kit.Items[0].Ok)
            return $"{kit.Items.FirstOrDefault()?.Findings.FirstOrDefault(f => f.Level == Furniture.Severity.Bad)?.Text ?? "the door can't be made to swing"} It's glued in for now.";
        return null;
    }

    /// <summary>The doors that swing in flat walls: their pieces (alike ones cut together), and each one hung in its doorway.</summary>
    static void HangDoors(BuildingSpec s, List<SwungDoor> doors, List<Piece> pieces, List<HouseSolid> solids, List<Finding> findings)
    {
        if (doors.Count == 0) return;
        double Tf = s.FurnitureSheetMm;
        var made = new Dictionary<(double, double, DoorShape, Furniture.HingeSide), (FurnitureItem Item, ItemKit Kit, List<SwungDoor> Where)>();
        foreach (var door in doors)
        {
            if (ClipDoor(s, door.Door) is not { } item) continue;
            var key = (item.WidthMm, item.HeightMm, item.Shape, item.Hinge);
            if (made.TryGetValue(key, out var m)) { m.Where.Add(door); continue; }
            var kit = FurnitureKit.Make(SetFor(s, [item]));
            if (kit.Items.Count == 0 || !kit.Items[0].Ok) continue;
            made[key] = (item, kit.Items[0], [door]);
        }
        int n = 0;
        foreach (var (_, (item, ik, where)) in made)
        {
            string label = made.Count == 1 ? "Door" : $"Door {++n}";
            string walls = string.Join(", ", where.Select(w => w.Level == 0 && !doors.Any(o => o.Level > 0) ? w.Wall : $"{w.Wall} (floor {w.Level + 1})"));
            foreach (var p in ik.Parts)
                pieces.Add(new Piece($"{label} (wall{(where.Count == 1 ? "" : "s")} {walls}): {p.Name.ToLowerInvariant()}", Sheet.Furniture, p.Cut, p.Etched, p.Lines, p.Copies * where.Count));
            foreach (var w in where)
            {
                double turn = Math.Atan2(-w.U.Z, w.U.X) * 180 / Math.PI;
                foreach (var p in ik.Parts)
                    foreach (var pose in p.Poses)
                    {
                        var at = Interior.Move(pose, w.At.X, w.At.Y, w.At.Z, turn);
                        Pose? open = null;
                        if (p.Name == "Door")
                        {
                            bool right = item.Hinge == Furniture.HingeSide.Right;
                            var pivot = at.At(right ? item.WidthMm : 0, 0);
                            open = Swung(at, pivot, right ? -ShownOpenDeg : ShownOpenDeg);
                        }
                        solids.Add(new HouseSolid($"Wall {w.Wall} door", p.Cut, at, Tf, Nil(p.Etched), Nil(p.Lines), w.Level, SolidKind.Furniture)
                        { Facing = w.Facing, Middle = w.Middle, Open = open });
                    }
            }
            foreach (var f in ik.Findings.Where(f => f.Level == Furniture.Severity.Note).Take(1))
                findings.Add(new(Severity.Note, $"{label}: {f.Text}"));
        }
        if (Tf > s.WallSheetMm + 1e-9)
            findings.Add(new(Severity.Note, "The doors that swing are cut from the furniture's sheet, which is thicker than the walls' here."));
    }

    /// <summary>A piece turned <paramref name="deg"/> about the upright line through <paramref name="pivot"/> (positive turns +X towards +Z).</summary>
    public static Pose Swung(Pose p, V3 pivot, double deg)
    {
        double a = deg * Math.PI / 180, c = Math.Cos(a), sn = Math.Sin(a);
        V3 R(V3 v) => new(v.X * c - v.Z * sn, v.Y, v.X * sn + v.Z * c);
        return new Pose(pivot + R(p.O - pivot), R(p.U), R(p.V));
    }
}
