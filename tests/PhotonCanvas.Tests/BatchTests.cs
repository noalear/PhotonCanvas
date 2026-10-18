using PhotonCanvas.Core;
using PhotonCanvas.Core.Camera;
using PhotonCanvas.Doc;
using Xunit;

namespace PhotonCanvas.Tests;

/// <summary>Finding pieces on the bed, and copies that each mark the next serial number or row.</summary>
public class BatchTests
{
    [Fact]
    public void PiecesAreFoundWithTheirAngle()
    {
        // 200 × 120 px at 0.5 mm: a level 40 × 20 mm bar and one turned 30°, plus a speck.
        int w = 200, h = 120;
        var keep = new byte[w * h];
        void Bar(double cx, double cy, double len, double wid, double deg)
        {
            double r = deg * Math.PI / 180, c = Math.Cos(r), s = Math.Sin(r);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    double dx = (x + 0.5) * 0.5 - cx, dy = (y + 0.5) * 0.5 - cy;
                    double a = dx * c + dy * s, b = -dx * s + dy * c;
                    if (Math.Abs(a) <= len / 2 && Math.Abs(b) <= wid / 2) keep[y * w + x] = 255;
                }
        }
        Bar(25, 20, 40, 20, 0);
        Bar(70, 35, 40, 20, 30);
        keep[100 * w + 10] = 255;
        var found = PieceFinder.Find(keep, w, h, new Rect2(0, 0, 100, 60), 0.5, minAreaMm2: 20);
        Assert.Equal(2, found.Count);
        Assert.Equal(25, found[0].Centre.X, 0);
        Assert.Equal(0, found[0].AngleDeg, 0);
        Assert.InRange(found[0].LengthMm, 39, 41.5);
        Assert.Equal(30, found[1].AngleDeg, 0);
        Assert.Equal(-30, PieceFinder.TurnOnto(90, 240), 6);
        Assert.Equal(10, PieceFinder.TurnOnto(-85, 105), 6);
    }

    [Fact]
    public void CopiesMarkTheNextNumbers()
    {
        var s = DocState.New();
        var text = TextShape.Create("No. {serial}", new TextStyle("Arial", 5), s.Layers[0].Id, Affine.Identity);
        var copies = Enumerable.Range(0, 3).Select(i => text.With(id: Guid.NewGuid(), transform: Affine.Translate(i * 30, 0)).WithVarOffset(i)).ToList();
        s = Ops.AddShapes(s, copies) with { Vars = new VariableData { Serial = 7, SerialStep = 2 } };
        s = VariableText.Refresh(s, DateTime.Now);
        Assert.Equal(["No. 7", "No. 9", "No. 11"], s.Shapes.OfType<TextShape>().Select(t => t.Shown));
        Assert.Equal(3, VariableText.ItemsPerRun(s));
        Assert.Equal("serials 7–11", VariableText.Describe(s));
        var next = VariableText.AfterRun(s, DateTime.Now);
        Assert.Equal(13, next.Vars.Serial);
        Assert.Equal("No. 13", next.Shapes.OfType<TextShape>().First().Shown);
        // The end is checked against the last copy.
        Assert.Contains(VariableText.Problems(s with { Vars = s.Vars with { SerialEnd = 10 } }), p => p.Contains("up to serial 11"));
        var back = DocFile.Deserialize(DocFile.Serialize(s));
        Assert.Equal([0, 1, 2], back.Shapes.Select(x => x.VarOffset));
    }

    [Fact]
    public void RebuiltShapesKeepWhatWasSetByHand()
    {
        var s = DocState.New();
        var text = (TextShape)TextShape.Create("No. {serial}", new TextStyle("Arial", 5), s.Layers[0].Id, Affine.Translate(10, 10)).WithLocked(true).WithVarOffset(2);
        var tabbed = (TextShape)text.WithTabs([new Vec2(1, 2)]).WithStart(new Vec2(3, 4));
        var refreshed = tabbed.WithShown("No. 9");
        Assert.True(refreshed.Locked);
        Assert.Equal(2, refreshed.VarOffset);
        Assert.Equal(new Vec2(1, 2), Assert.Single(refreshed.ManualTabs));
        Assert.Equal(new Vec2(3, 4), refreshed.StartPoint);
        // An editable shape that folds its scale in keeps its tab where it was on the work area.
        var rect = (ParamShape)ParamShape.Create(ParamKind.Rectangle, 20, 10, 0, 4, 0.5, s.Layers[0].Id, Affine.Identity).With(transform: Affine.Scale(2, 1));
        var withTab = (ParamShape)rect.WithTabs([new Vec2(10, 0)]);
        var edited = withTab.Edit(40, 10, 0, 4, 0.5);
        Assert.Equal(new Vec2(20, 0), Assert.Single(edited.TabsWorld));
    }
}
