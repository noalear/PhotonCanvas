using PhotonCanvas.Addon.DeviceArray;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Extensions;
using Xunit;

namespace PhotonCanvas.Tests;

/// <summary>The device array add-on: a matrix of devices at a pitch, numbered ahead, any one turned off, kept in step on every edit.</summary>
public class DeviceArrayTests
{
    static (DocState State, PathShape Square) OneSquare()
    {
        var s = DocState.New();
        var sq = new PathShape { Name = "Tag", LayerId = s.Layers[0].Id, Geometry = PathGeometry.Rect(10, 10, 10, 10) };
        return (Ops.AddShapes(s, [sq]), sq);
    }

    static (DocState State, TextShape Text) OneSerial(long serial = 1)
    {
        var s = DocState.New() with { Vars = new VariableData { Serial = serial } };
        var t = TextShape.Create("SN {serial}", new TextStyle("Arial", 5), s.Layers[0].Id, Affine.Translate(10, 10));
        return (VariableText.Refresh(Ops.AddShapes(s, [t]), DateTime.Now), (TextShape)VariableText.Refresh(Ops.AddShapes(s, [t]), DateTime.Now).Shapes[0]);
    }

    static Shape At(DocState s, Guid array, Cell c) =>
        s.Shapes.Single(x => DeviceArrays.ArrayOf(x) == array && DeviceArrays.CellOf(x) == c);

    static Rect2 StepBox(BuiltJob job) =>
        job.Steps.SelectMany(st => st.Points).Aggregate(Rect2.Empty, (r, p) => r.Union(new Rect2(p.X, p.Y, p.X, p.Y)));

    [Fact]
    public void TheDesignIsCopiedOntoEveryDeviceAtThePitch()
    {
        var (s, sq) = OneSquare();
        var (made, id) = DeviceArrays.Create(s, new HashSet<Guid> { sq.Id }, new ArraySpec { Columns = 3, Rows = 2, PitchX = 20, PitchY = 15 });
        Assert.Equal(6, made.Shapes.Count);
        var first = At(made, id, new Cell(0, 0));
        Assert.Equal(sq.Id, first.Id);
        Assert.Equal(new Rect2(10, 10, 20, 20), first.Bounds);
        Assert.Equal(new Rect2(50, 25, 60, 35), At(made, id, new Cell(2, 1)).Bounds);
        // Counted across, row by row.
        Assert.Equal([0, 1, 2, 3, 4, 5], new ArraySpec { Columns = 3, Rows = 2 }.Cells().Select(c => At(made, id, c).VarOffset));
        var view = DeviceArrays.Find(made, id)!;
        Assert.Equal(5, view.Copies);
        Assert.Equal(new Rect2(10, 10, 60, 35), view.Bounds);
        Assert.Equal(new Rect2(5, 7.5, 25, 22.5), view.CellBox(new Cell(0, 0)));
        // Every device goes in the job.
        Assert.Equal(new Rect2(10, 10, 60, 35), StepBox(JobBuilder.Build(made, new JobOptions())));
    }

    [Fact]
    public void ADeviceThatIsOffIsNotMarkedAndUsesNoNumber()
    {
        var (s, t) = OneSerial(serial: 100);
        var spec = new ArraySpec { Columns = 3, Rows = 1, PitchX = 30, PitchY = 10, Off = [new Cell(1, 0)] };
        var (made, id) = DeviceArrays.Create(s, new HashSet<Guid> { t.Id }, spec);
        string Shown(Cell c) => ((TextShape)At(made, id, c)).Shown!;
        Assert.Equal("SN 100", Shown(new Cell(0, 0)));
        Assert.Equal("SN 101", Shown(new Cell(2, 0)));
        Assert.False(At(made, id, new Cell(1, 0)).Output);
        Assert.Equal(2, VariableText.ItemsPerRun(made));
        Assert.Equal("serials 100–101", VariableText.Describe(made));
        // The device that's off isn't in the job: only the first and third.
        var job = StepBox(JobBuilder.Build(made, new JobOptions()));
        Assert.True(job.MinX < 20 && job.MaxX > 70);
        var withoutOff = JobBuilder.Build(made, new JobOptions()).Steps.SelectMany(st => st.Points).Where(p => p.X > 38 && p.X < 62);
        Assert.Empty(withoutOff);
        // After the run the next tray starts after the last one marked.
        Assert.Equal(102, VariableText.AfterRun(made, DateTime.Now).Vars.Serial);

        // Dragging the first device: the copies keep their own numbers and move with it.
        var before = At(made, id, new Cell(2, 0)).Bounds;
        var dragged = new ArrayRule().Apply(made, Ops.Move(made, new HashSet<Guid> { t.Id }, 4, 2));
        Assert.Equal("SN 101", ((TextShape)At(dragged, id, new Cell(2, 0))).Shown);
        Assert.Equal(before.MinX + 4, At(dragged, id, new Cell(2, 0)).Bounds.MinX, 6);
        Assert.Equal(before.MinY + 2, At(dragged, id, new Cell(2, 0)).Bounds.MinY, 6);
        // New words on the first device reach every device.
        var reworded = (TextShape)TextShape.Create("No. {serial}", new TextStyle("Arial", 5), t.LayerId, Affine.Translate(10, 10));
        var first = (TextShape)At(made, id, new Cell(0, 0));
        var edited = made with { Shapes = made.Shapes.Replace(first, reworded.With(id: first.Id).WithAddonData(first.AddonData)) };
        Assert.Equal("No. 101", ((TextShape)At(new ArrayRule().Apply(made, edited), id, new Cell(2, 0))).Shown);

        // Keeping their numbers: the third device is 102 and the whole tray is used up.
        var kept = DeviceArrays.SetSpec(made, id, spec with { KeepNumbers = true });
        Assert.Equal("SN 102", ((TextShape)At(kept, id, new Cell(2, 0))).Shown);
        Assert.Equal(3, VariableText.ItemsPerRun(kept));
    }

    [Fact]
    public void TheOrdersCountAsTheySay()
    {
        var spec = new ArraySpec { Columns = 3, Rows = 2 };
        int[] Ranks(CountOrder o) => [.. (spec with { Order = o }).Cells().Select(c => c.C * 10 + c.R)];
        Assert.Equal([0, 10, 20, 1, 11, 21], Ranks(CountOrder.Across));
        Assert.Equal([0, 1, 10, 11, 20, 21], Ranks(CountOrder.Down));
        Assert.Equal([0, 10, 20, 21, 11, 1], Ranks(CountOrder.AcrossBackAndForth));
        Assert.Equal([0, 1, 11, 10, 20, 21], Ranks(CountOrder.DownBackAndForth));
    }

    [Fact]
    public void TheMatrixIsKeptInTheDesignFile()
    {
        var spec = new ArraySpec { Columns = 4, Rows = 3, PitchX = 12.5, PitchY = 8.25, Order = CountOrder.DownBackAndForth, KeepNumbers = true, Off = [new Cell(1, 2), new Cell(3, 0)] };
        Assert.Equal(spec.Write(), ArraySpec.Read(spec.Write()).Write());
        Assert.Equal(spec.Off, ArraySpec.Read(spec.Write()).Off);
        // Off devices outside the matrix and silly sizes are dropped.
        var odd = ArraySpec.Read("cols=900;rows=0;px=abc;off=7,7 0,0");
        Assert.Equal(ArraySpec.MaxSide, odd.Columns);
        Assert.Equal(1, odd.Rows);
        Assert.Equal([new Cell(0, 0)], odd.Off);

        var (s, sq) = OneSquare();
        var (made, id) = DeviceArrays.Create(s, new HashSet<Guid> { sq.Id }, spec);
        var back = DocFile.Deserialize(DocFile.Serialize(made));
        Assert.Equal(spec.Write(), DeviceArrays.Find(back, id)!.Spec.Write());
        Assert.Equal(made.Shapes.Select(x => x.Output), back.Shapes.Select(x => x.Output));
        Assert.Equal(made.Shapes.Select(x => x.VarOffset), back.Shapes.Select(x => x.VarOffset));
    }

    [Fact]
    public void FillingAnAreaFitsWholeDevices()
    {
        // 10 mm devices every 15 mm in 100 mm: 0, 15 … 90 → 7 (the 7th ends at 100).
        Assert.Equal(7, DeviceArrayWindow.Fit(100, 10, 15));
        Assert.Equal(6, DeviceArrayWindow.Fit(99.9, 10, 15));
        Assert.Equal(1, DeviceArrayWindow.Fit(5, 10, 15));
        Assert.Equal(1, DeviceArrayWindow.Fit(100, 10, 0));
    }

    [Fact]
    public void CopiesFollowTheFirstDevice()
    {
        var rule = new ArrayRule();
        var (s, sq) = OneSquare();
        var (made, id) = DeviceArrays.Create(s, new HashSet<Guid> { sq.Id }, new ArraySpec { Columns = 2, Rows = 2, PitchX = 20, PitchY = 20 });
        var copyId = At(made, id, new Cell(1, 1)).Id;
        var moved = Ops.Move(made, new HashSet<Guid> { sq.Id }, 5, 0);
        var kept = rule.Apply(made, moved);
        Assert.Equal(new Rect2(35, 30, 45, 40), At(kept, id, new Cell(1, 1)).Bounds);
        Assert.Equal(copyId, At(kept, id, new Cell(1, 1)).Id);
        Assert.Equal(4, kept.Shapes.Count);
        // Nothing to do: the same state back.
        Assert.Same(kept, rule.Apply(moved, kept));
    }

    [Fact]
    public void MovingAnyDeviceMovesTheWholeArray()
    {
        var rule = new ArrayRule();
        var (s, sq) = OneSquare();
        var (made, id) = DeviceArrays.Create(s, new HashSet<Guid> { sq.Id }, new ArraySpec { Columns = 3, Rows = 1, PitchX = 20, PitchY = 20 });
        var copy = At(made, id, new Cell(2, 0));
        var dragged = Ops.Move(made, new HashSet<Guid> { copy.Id }, 0, 7);
        var kept = rule.Apply(made, dragged);
        Assert.Equal(new Rect2(10, 17, 20, 27), At(kept, id, new Cell(0, 0)).Bounds);
        Assert.Equal(new Rect2(30, 17, 40, 27), At(kept, id, new Cell(1, 0)).Bounds);
        Assert.Equal(new Rect2(50, 17, 60, 27), At(kept, id, new Cell(2, 0)).Bounds);
        Assert.Equal(sq.Id, At(kept, id, new Cell(0, 0)).Id);
    }

    [Fact]
    public void SwitchingOneDeviceOffLeavesTheOthersAlone()
    {
        var rule = new ArrayRule();
        var s = DocState.New();
        var a = new PathShape { LayerId = s.Layers[0].Id, Geometry = PathGeometry.Rect(10, 10, 10, 10) };
        var b = new PathShape { LayerId = s.Layers[0].Id, Geometry = PathGeometry.Rect(12, 12, 2, 2) };
        s = Ops.AddShapes(s, [a, b]);
        var (made, id) = DeviceArrays.Create(s, new HashSet<Guid> { a.Id, b.Id }, new ArraySpec { Columns = 3, Rows = 1, PitchX = 20 });
        var copy = made.Shapes.Single(x => DeviceArrays.CellOf(x) == new Cell(1, 0) && DeviceArrays.PartOf(x) == DeviceArrays.PartOf(At2(made, a.Id)));
        // The lightning switched off on one shape of the middle device: the whole device is off.
        var off = made with { Shapes = made.Shapes.Replace(copy, copy.WithOutput(false)) };
        var kept = rule.Apply(made, off);
        var spec = DeviceArrays.Find(kept, id)!.Spec;
        Assert.False(spec.IsOn(new Cell(1, 0)));
        Assert.True(spec.IsOn(new Cell(0, 0)) && spec.IsOn(new Cell(2, 0)));
        Assert.All(kept.Shapes.Where(x => DeviceArrays.CellOf(x) == new Cell(1, 0)), x => Assert.False(x.Output));
        Assert.All(kept.Shapes.Where(x => DeviceArrays.CellOf(x) != new Cell(1, 0)), x => Assert.True(x.Output));
        // Hiding a copy does the same; the first device's own switch turns it off.
        var firstOff = rule.Apply(kept, kept with { Shapes = kept.Shapes.Replace(At2(kept, a.Id), At2(kept, a.Id).WithOutput(false)) });
        Assert.False(DeviceArrays.Find(firstOff, id)!.Spec.IsOn(new Cell(0, 0)));
        Assert.False(At2(firstOff, b.Id).Output);
        // And on again.
        var c2 = firstOff.Shapes.First(x => DeviceArrays.CellOf(x) == new Cell(1, 0));
        var back = rule.Apply(firstOff, firstOff with { Shapes = firstOff.Shapes.Replace(c2, c2.WithOutput(true)) });
        Assert.True(DeviceArrays.Find(back, id)!.Spec.IsOn(new Cell(1, 0)));

        static Shape At2(DocState st, Guid sid) => st.Shapes.Single(x => x.Id == sid);
    }

    [Fact]
    public void AGuideStaysUnmarkedOnEveryDevice()
    {
        var s = DocState.New();
        var outline = new PathShape { LayerId = s.Layers[0].Id, Geometry = PathGeometry.Rect(0, 0, 30, 30) }.WithOutput(false);
        var mark = new PathShape { LayerId = s.Layers[0].Id, Geometry = PathGeometry.Rect(10, 10, 10, 10) };
        s = Ops.AddShapes(s, [outline, mark]);
        var (made, id) = DeviceArrays.Create(s, new HashSet<Guid> { outline.Id, mark.Id }, new ArraySpec { Columns = 2, Rows = 2, PitchX = 35, PitchY = 35 });
        var guides = made.Shapes.Where(x => DeviceArrays.PartOf(x) == DeviceArrays.PartOf(made.Shapes.Single(y => y.Id == outline.Id))).ToList();
        Assert.Equal(4, guides.Count);
        Assert.All(guides, g => Assert.False(g.Output));
        Assert.Equal(4, made.Shapes.Count(x => x.Output));
        // Removing the array gives back the design as it was, the guide still a guide.
        var removed = DeviceArrays.Remove(made, id);
        Assert.Equal(2, removed.Shapes.Count);
        Assert.False(removed.Shapes.Single(x => x.Id == outline.Id).Output);
        Assert.True(removed.Shapes.Single(x => x.Id == mark.Id).Output);
        Assert.DoesNotContain(removed.Shapes, DeviceArrays.IsTagged);
    }

    [Fact]
    public void DeletingACopyTurnsItsDeviceOffAndDeletingTheDesignTakesTheArray()
    {
        var rule = new ArrayRule();
        var (s, sq) = OneSquare();
        var (made, id) = DeviceArrays.Create(s, new HashSet<Guid> { sq.Id }, new ArraySpec { Columns = 3, Rows = 1, PitchX = 20 });
        var copy = At(made, id, new Cell(1, 0));
        var kept = rule.Apply(made, Ops.Delete(made, new HashSet<Guid> { copy.Id }));
        Assert.Equal(3, kept.Shapes.Count);
        Assert.False(At(kept, id, new Cell(1, 0)).Output);
        Assert.False(DeviceArrays.Find(kept, id)!.Spec.IsOn(new Cell(1, 0)));

        var gone = rule.Apply(kept, Ops.Delete(kept, new HashSet<Guid> { sq.Id }));
        Assert.Empty(gone.Shapes);
    }

    [Fact]
    public void PastedCopiesBecomePlainShapesOrASecondArray()
    {
        var rule = new ArrayRule();
        var (s, sq) = OneSquare();
        var (made, id) = DeviceArrays.Create(s, new HashSet<Guid> { sq.Id }, new ArraySpec { Columns = 2, Rows = 1, PitchX = 20 });
        // One copy pasted on its own: a plain shape where it was put.
        var copy = At(made, id, new Cell(1, 0));
        var (pasted, _) = Ops.Duplicate(made, new HashSet<Guid> { copy.Id }, 0, 40);
        var plain = rule.Apply(made, pasted);
        Assert.Equal(3, plain.Shapes.Count);
        Assert.Single(plain.Shapes, x => !DeviceArrays.IsTagged(x));
        Assert.Equal(new Rect2(30, 50, 40, 60), plain.Shapes.Single(x => !DeviceArrays.IsTagged(x)).Bounds);
        // The whole array duplicated: a second array of its own.
        var all = made.Shapes.Select(x => x.Id).ToHashSet();
        var (twice, _) = Ops.Duplicate(made, all, 0, 40);
        var two = rule.Apply(made, twice);
        Assert.Equal(4, two.Shapes.Count);
        Assert.Equal(2, DeviceArrays.Arrays(two).Count());
        var second = DeviceArrays.Arrays(two).Single(a => a != id);
        Assert.Equal(new Rect2(10, 50, 20, 60), At(two, second, new Cell(0, 0)).Bounds);
    }

    [Fact]
    public void EditsThroughTheDocumentAreOneUndoStep()
    {
        using var rule = DocRules.Add(new ArrayRule());
        var (s, sq) = OneSquare();
        var (made, id) = DeviceArrays.Create(s, new HashSet<Guid> { sq.Id }, new ArraySpec { Columns = 2, Rows = 1, PitchX = 20 });
        var doc = new Document(made);
        doc.Commit("Move", Ops.Move(doc.State, new HashSet<Guid> { sq.Id }, 3, 0));
        Assert.Equal(new Rect2(33, 10, 43, 20), At(doc.State, id, new Cell(1, 0)).Bounds);
        // Dragging a copy moves them all, live.
        doc.BeginEdit();
        var copy = At(doc.State, id, new Cell(1, 0));
        doc.UpdateEdit(Ops.Move(doc.State, new HashSet<Guid> { copy.Id }, 0, 5));
        Assert.Equal(new Rect2(13, 15, 23, 25), At(doc.State, id, new Cell(0, 0)).Bounds);
        doc.EndEdit("Drag");
        doc.Undo();
        Assert.Equal(new Rect2(13, 10, 23, 20), At(doc.State, id, new Cell(0, 0)).Bounds);
        doc.Undo();
        Assert.Equal(new Rect2(30, 10, 40, 20), At(doc.State, id, new Cell(1, 0)).Bounds);
    }

    [Fact]
    public void ReleasingKeepsEveryDeviceAsPlainShapes()
    {
        var (s, sq) = OneSquare();
        var (made, id) = DeviceArrays.Create(s, new HashSet<Guid> { sq.Id }, new ArraySpec { Columns = 3, Rows = 1, PitchX = 20, Off = [new Cell(2, 0)] });
        var plain = DeviceArrays.Release(made, id);
        Assert.Equal(3, plain.Shapes.Count);
        Assert.DoesNotContain(plain.Shapes, DeviceArrays.IsTagged);
        Assert.False(plain.Shapes[2].Output);
    }
}
