using Avalonia.Media;
using Clipper2Lib;
using PhotonCanvas.App.View3D;

namespace PhotonCanvas.Tests;

/// <summary>
/// The 3D views' painter (used by the bridge, stairs, furniture, scatter, game kit, building and roof makers):
/// whatever is nearest shows, however the model is turned, including where a long face lies beside a small
/// one or pieces pass through each other (where drawing whole faces far to near goes wrong).
/// </summary>
public class Model3DPainterTests
{
    const int W = 400, H = 400;
    static readonly Color Bg = Colors.Black;
    static readonly Color Red = Color.FromRgb(220, 40, 40), Blue = Color.FromRgb(40, 60, 220), Green = Color.FromRgb(40, 200, 60), Gold = Color.FromRgb(230, 200, 40);

    /// <summary>A piece painted all one colour, with no lines round it.</summary>
    static SlabStyle Plain(Color c) => new((_, _) => c, Colors.Transparent, LineWidth.Px(0), Gold, Gold, LineWidth.Px(1));

    static PathsD Rect(double u0, double v0, double u1, double v1) => [new PathD { new(u0, v0), new(u1, v0), new(u1, v1), new(u0, v1) }];

    static Color At(uint[] picture, Model3DPainter painter, Vec3 p)
    {
        var s = painter.ToView(p);
        uint c = picture[(int)s.Y * W + (int)s.X];
        return Color.FromRgb((byte)(c >> 16), (byte)(c >> 8), (byte)c);
    }

    static void Near(Color want, Color got)
    {
        Assert.True(Math.Abs(want.R - got.R) + Math.Abs(want.G - got.G) + Math.Abs(want.B - got.B) < 30, $"wanted {want}, got {got}");
    }

    [Fact]
    public void A_small_piece_standing_at_the_back_of_a_long_one_shows_in_front_of_it()
    {
        // A big floor, and a small card standing on it near the back. Sorted by their middles, the floor is
        // nearer (its middle is), so drawing far to near paints the floor over the card's foot.
        var scene = new Scene3D();
        scene.AddSlab(Rect(-50, -50, 50, 50), new Frame3(new(0, 0, 0), new(1, 0, 0), new(0, 0, -1)), -1, 0, Plain(Blue));
        scene.AddSlab(Rect(-5, 0, 5, 20), new Frame3(new(0, 0, -40), new(1, 0, 0), new(0, 1, 0)), -0.5, 0.5, Plain(Red));
        var painter = new Model3DPainter();
        var pic = painter.Picture(scene, Camera3D.YUp(0, 30), W, H, Bg)!;
        Near(Red, At(pic, painter, new(0, 2, -39.5)));
        Near(Red, At(pic, painter, new(3, 15, -39.5)));
        Near(Blue, At(pic, painter, new(20, 0, -40)));
        Near(Blue, At(pic, painter, new(0, 0, 30)));
    }

    [Fact]
    public void Pieces_through_each_other_show_whichever_is_nearer_on_each_side()
    {
        // Two cards crossing in an X, their middles the same distance away: each is in front on one side.
        var scene = new Scene3D();
        scene.AddSlab(Rect(-20, 0, 20, 20), new Frame3(new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)), -0.5, 0.5, Plain(Red));
        scene.AddSlab(Rect(-20, 0, 20, 20), new Frame3(new(0, 0, 0), new(0, 0, -1), new(0, 1, 0)), -0.5, 0.5, Plain(Blue));
        var painter = new Model3DPainter();
        for (int turn = 0; turn < 2; turn++)
        {
            var pic = painter.Picture(scene, Camera3D.YUp(45, 0), W, H, Bg)!;
            // Left of the crossing the blue card (towards you) is nearer; right of it, the red one.
            Near(Blue, At(pic, painter, new(0.5, 10, 10)));
            Near(Red, At(pic, painter, new(10, 10, 0.5)));
            // The same picture again (nothing changed): the same answer, not one that flickers.
        }
    }

    [Fact]
    public void Each_face_shows_nearest_whichever_way_it_is_turned()
    {
        // A box of six coloured walls round a red block in the middle: from every side, what shows in the
        // middle of the picture is the box's near wall, never the block or the far wall.
        var scene = new Scene3D();
        scene.AddSlab(Rect(-5, -5, 5, 5), new Frame3(new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)), -5, 5, Plain(Red));
        foreach (var (o, u, v) in new (Vec3, Vec3, Vec3)[]
                 {
                     (new(0, 0, 20), new(1, 0, 0), new(0, 1, 0)), (new(0, 0, -20), new(-1, 0, 0), new(0, 1, 0)),
                     (new(20, 0, 0), new(0, 0, -1), new(0, 1, 0)), (new(-20, 0, 0), new(0, 0, 1), new(0, 1, 0)),
                 })
            scene.AddSlab(Rect(-21, -21, 21, 21), new Frame3(o, u, v), -1, 0, Plain(Green));
        var painter = new Model3DPainter();
        for (int yaw = 0; yaw < 360; yaw += 15)
        {
            var pic = painter.Picture(scene, Camera3D.YUp(yaw, 0), W, H, Bg)!;
            uint c = pic[(H / 2) * W + W / 2];
            Near(Green, Color.FromRgb((byte)(c >> 16), (byte)(c >> 8), (byte)c));
        }
    }

    [Fact]
    public void Engraving_shows_only_where_its_face_does()
    {
        // An engraved floor with a card standing in front of part of the engraving.
        var scene = new Scene3D();
        PathsD etched = Rect(-30, -30, 30, 30);
        scene.AddSlab(Rect(-50, -50, 50, 50), new Frame3(new(0, 0, 0), new(1, 0, 0), new(0, 0, -1)), -1, 0, Plain(Blue), etched);
        scene.AddSlab(Rect(-10, 0, 10, 40), new Frame3(new(0, 0, 35), new(1, 0, 0), new(0, 1, 0)), -0.5, 0.5, Plain(Red));
        var painter = new Model3DPainter();
        var pic = painter.Picture(scene, Camera3D.YUp(0, 30), W, H, Bg)!;
        Near(Gold, At(pic, painter, new(20, 0, 20)));
        Near(Blue, At(pic, painter, new(40, 0, 40)));
        // Behind the card: the card, not the engraving over it.
        Near(Red, At(pic, painter, new(0, 20, 35.5)));
    }

    [Fact]
    public void Ghosts_are_seen_through_and_hidden_groups_are_left_out()
    {
        var scene = new Scene3D();
        scene.AddSlab(Rect(-20, 0, 20, 20), new Frame3(new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)), -0.5, 0.5, Plain(Red));
        // A see-through blue wall in front of the red card, and a green one in front of that in group 1.
        var ghost = new SlabStyle((_, _) => Color.FromArgb(0x80, Blue.R, Blue.G, Blue.B), Colors.Transparent, LineWidth.Px(0), Gold, Gold, LineWidth.Px(1));
        scene.AddSlab(Rect(-30, -5, 30, 30), new Frame3(new(0, 0, 10), new(1, 0, 0), new(0, 1, 0)), -0.5, 0.5, ghost);
        scene.AddSlab(Rect(-30, -5, 30, 30), new Frame3(new(0, 0, 20), new(1, 0, 0), new(0, 1, 0)), -0.5, 0.5, Plain(Green), group: 1);
        var painter = new Model3DPainter();
        var pic = painter.Picture(scene, Camera3D.YUp(0, 0), W, H, Bg, hiddenGroups: 1UL << 1)!;
        var mixed = Color.FromRgb((byte)((Red.R + Blue.R) / 2), (byte)((Red.G + Blue.G) / 2), (byte)((Red.B + Blue.B) / 2));
        Near(mixed, At(pic, painter, new(0, 10, 0.5)));
        Near(Color.FromRgb((byte)(Blue.R / 2), (byte)(Blue.G / 2), (byte)(Blue.B / 2)), At(pic, painter, new(25, 25, 10.5)));
        pic = painter.Picture(scene, Camera3D.YUp(0, 0), W, H, Bg)!;
        Near(Green, At(pic, painter, new(0, 10, 0.5)));
    }

    // ---- the camera: fitted, zoomed and moved ----

    /// <summary>A floor with a card standing on it, as in the tests above.</summary>
    static Scene3D FloorAndCard()
    {
        var scene = new Scene3D();
        scene.AddSlab(Rect(-50, -50, 50, 50), new Frame3(new(0, 0, 0), new(1, 0, 0), new(0, 0, -1)), -1, 0, Plain(Blue));
        scene.AddSlab(Rect(-5, 0, 5, 20), new Frame3(new(0, 0, -40), new(1, 0, 0), new(0, 1, 0)), -0.5, 0.5, Plain(Red));
        return scene;
    }

    static void Same(Avalonia.Point want, Avalonia.Point got, double within = 0.01)
    {
        Assert.True(Math.Abs(want.X - got.X) < within && Math.Abs(want.Y - got.Y) < within, $"wanted {want}, got {got}");
    }

    [Fact]
    public void Fitted_the_whole_model_is_in_the_view_inside_its_margins()
    {
        var painter = new Model3DPainter();
        var cam = Camera3D.YUp(32, 24);
        Assert.NotNull(painter.Picture(FloorAndCard(), cam, W, H, Bg));
        Assert.Equal(painter.Fitted, painter.Shown);
        foreach (var x in new[] { -50.0, 50 })
            foreach (var y in new[] { -1.0, 0, 20 })
                foreach (var z in new[] { -50.0, 50 })
                {
                    var p = painter.ToView(new Vec3(x, y, z));
                    Assert.InRange(p.X, 14.9, W - 14.9);
                    Assert.InRange(p.Y, 13.9, H - 39.9);
                }
        // Its target, the middle of the model, is in the middle of the room the margins leave.
        Same(Model3DPainter.Middle(new Avalonia.Size(W, H)), painter.ToView(painter.Fitted.Target));
    }

    [Fact]
    public void A_framing_puts_its_target_in_the_middle_at_its_size()
    {
        var painter = new Model3DPainter();
        var cam = Camera3D.YUp(32, 24);
        var target = new Vec3(0, 10, -40);
        var pic = painter.Picture(FloorAndCard(), cam, W, H, Bg, framing: new Framing(20, target))!;
        Assert.Equal(20, painter.PxPerMm, 6);
        Same(Model3DPainter.Middle(new Avalonia.Size(W, H)), painter.ToView(target));
        // Zoomed in on the card, it fills the middle of the view; the floor's corners are far off it.
        Near(Red, At(pic, painter, target + new Vec3(0, 0, 0.5)));
        var far = painter.ToView(new Vec3(-50, 0, -50));
        Assert.True(far.X < 0 || far.X > W || far.Y < 0 || far.Y > H, $"{far}");
        // 1 mm across the screen is 20 screen units.
        Assert.Equal(20, painter.ToView(target + cam.Right).X - painter.ToView(target).X, 6);
    }

    [Fact]
    public void Zooming_keeps_the_point_under_the_pointer_where_it_is()
    {
        var painter = new Model3DPainter();
        var cam = Camera3D.YUp(40, 30);
        var scene = FloorAndCard();
        painter.Picture(scene, cam, W, H, Bg);
        var fitted = painter.Shown;
        var middle = Model3DPainter.Middle(new Avalonia.Size(W, H));
        // A point on the floor, its place on the screen, and the framing zoomed 3 times about it.
        var corner = new Vec3(40, 0, 40);
        var at = painter.ToView(corner);
        var zoomed = fitted.Zoomed(cam, fitted.PxPerMm * 3, at - middle);
        painter.Picture(scene, cam, W, H, Bg, framing: zoomed);
        Same(at, painter.ToView(corner), 1e-6);
        Assert.Equal(fitted.PxPerMm * 3, painter.PxPerMm, 6);
        // Anything else has moved away from it, three times as far.
        var other = painter.ToView(new Vec3(0, 0, 0));
        painter.Picture(scene, cam, W, H, Bg);
        var was = painter.ToView(new Vec3(0, 0, 0));
        Assert.Equal((was.X - at.X) * 3, other.X - at.X, 6);
        Assert.Equal((was.Y - at.Y) * 3, other.Y - at.Y, 6);
    }

    [Fact]
    public void Moving_moves_the_picture_and_turning_turns_it_about_the_middle()
    {
        var painter = new Model3DPainter();
        var cam = Camera3D.YUp(40, 30);
        var scene = FloorAndCard();
        var f = new Framing(5, new Vec3(10, 0, 10));
        var p = new Vec3(-30, 0, 20);
        painter.Picture(scene, cam, W, H, Bg, framing: f);
        var before = painter.ToView(p);
        painter.Picture(scene, cam, W, H, Bg, framing: f.Moved(cam, new Avalonia.Vector(25, -12)));
        var after = painter.ToView(p);
        Same(new Avalonia.Point(before.X + 25, before.Y - 12), after, 1e-6);
        // Turned, the target stays in the middle (it turns about it) and the size stays the same.
        var turned = Camera3D.YUp(100, 50);
        painter.Picture(scene, turned, W, H, Bg, framing: f);
        Same(Model3DPainter.Middle(new Avalonia.Size(W, H)), painter.ToView(f.Target));
        Assert.Equal(5, painter.PxPerMm, 6);
    }
}
