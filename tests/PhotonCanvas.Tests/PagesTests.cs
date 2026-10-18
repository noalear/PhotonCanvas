using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using Xunit;

namespace PhotonCanvas.Tests;

public class PagesTests
{
    static DocState WithRect(DocState s, double x) =>
        Ops.AddShapes(s, [new PathShape { LayerId = s.Layers[0].Id, Geometry = PathGeometry.Rect(x, 0, 10, 10) }]);

    [Fact]
    public void PagesKeepTheirOwnShapes()
    {
        var s = WithRect(DocState.New(), 0);
        var first = s.Shapes[0].Id;
        s = Pages.Add(s);
        Assert.Equal(["Page 1", "Page 2"], Pages.Names(s));
        Assert.Empty(s.Shapes);
        Assert.Equal("Cut", Assert.Single(s.Layers).Name);
        s = WithRect(s, 50);
        s = WithRect(s, 70);
        s = Pages.SwitchTo(s, 0);
        Assert.Equal(first, Assert.Single(s.Shapes).Id);
        s = Pages.SwitchTo(s, 1);
        Assert.Equal(2, s.Shapes.Count);
        // The job is the page on screen.
        Assert.Equal(2, JobBuilder.Build(s, new JobOptions()).Job.Blocks.Count);
    }

    [Fact]
    public void DuplicateMoveRenameDelete()
    {
        var s = WithRect(DocState.New(), 0);
        s = Pages.Duplicate(s);
        Assert.Equal(["Page 1", "Page 1 copy"], Pages.Names(s));
        Assert.NotEqual(s.OtherPages[0].Shapes[0].Id, s.Shapes[0].Id);
        Assert.Equal(s.Layers[0].Id, s.Shapes[0].LayerId);
        s = Pages.Rename(Pages.Move(s, -1), "Back");
        Assert.Equal(["Back", "Page 1"], Pages.Names(s));
        s = Pages.Delete(s);
        Assert.Equal(["Page 1"], Pages.Names(s));
        Assert.Same(s, Pages.Delete(s)); // the last page stays
    }

    [Fact]
    public void PagesAreSavedWithTheProject()
    {
        var s = Pages.Add(WithRect(DocState.New(), 0), "Lid");
        s = WithRect(WithRect(s, 30), 60);
        var back = DocFile.Deserialize(DocFile.Serialize(s));
        Assert.Equal(["Page 1", "Lid"], Pages.Names(back));
        Assert.Equal(1, back.PageIndex);
        Assert.Equal(2, back.Shapes.Count);
        Assert.Single(back.OtherPages[0].Shapes);
        // Undo brings the page back as it was.
        var d = new Document();
        d.Reset(s, null);
        d.Commit("Show", Pages.SwitchTo(d.State, 0));
        Assert.Equal("Page 1", d.State.PageName);
        d.Undo();
        Assert.Equal("Lid", d.State.PageName);
        Assert.Equal(2, d.State.Shapes.Count);
    }
}
