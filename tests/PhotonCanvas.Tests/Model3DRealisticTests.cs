using Avalonia.Media;
using Clipper2Lib;
using PhotonCanvas.App.View3D;

namespace PhotonCanvas.Tests;

/// <summary>
/// The 3D views' realistic look: the same pieces in the same places as the plain look, made of wood and lit by a
/// sun that casts shadows on them and on the table they stand on.
/// </summary>
public class Model3DRealisticTests
{
    const int W = 500, H = 500;
    static readonly Color Bg = Color.FromRgb(240, 240, 240);
    static readonly Color Red = Color.FromRgb(220, 40, 40), Blue = Color.FromRgb(40, 60, 220), Gold = Color.FromRgb(230, 200, 40);

    static SlabStyle Plain(Color c) => new((_, _) => c, Colors.Transparent, LineWidth.Px(0), Gold, Gold, LineWidth.Px(1));

    static PathsD Rect(double u0, double v0, double u1, double v1) => [new PathD { new(u0, v0), new(u1, v0), new(u1, v1), new(u0, v1) }];

    /// <summary>A floor 200 mm square (its top at y = 0), and a pillar 10 × 10 × 40 mm standing in the middle of it.</summary>
    static Scene3D FloorAndPillar(bool floor = true)
    {
        var scene = new Scene3D();
        if (floor) scene.AddSlab(Rect(-100, -100, 100, 100), new Frame3(new(0, 0, 0), new(1, 0, 0), new(0, 0, -1)), -2, 0, Plain(Blue));
        scene.AddSlab(Rect(-5, 0, 5, 40), new Frame3(new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)), -5, 5, Plain(Red));
        return scene;
    }

    /// <summary>The average brightness (0–255) of the 5 × 5 pixels round where <paramref name="p"/> is drawn.</summary>
    static double Brightness(uint[] picture, Model3DPainter painter, Vec3 p)
    {
        var s = painter.ToView(p);
        double sum = 0;
        for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
            {
                uint c = picture[((int)s.Y + dy) * W + (int)s.X + dx];
                sum += ((c >> 16 & 0xFF) + (c >> 8 & 0xFF) + (c & 0xFF)) / 3.0;
            }
        return sum / 25;
    }

    static (double R, double G, double B) Colour(uint[] picture, Model3DPainter painter, Vec3 p)
    {
        var s = painter.ToView(p);
        uint c = picture[(int)s.Y * W + (int)s.X];
        return (c >> 16 & 0xFF, c >> 8 & 0xFF, c & 0xFF);
    }

    /// <summary>Where on the floor (y = 0) the shadow of the point <paramref name="height"/> mm up the pillar's middle falls.</summary>
    static Vec3 ShadowOf(Vec3 sun, double height) => new Vec3(0, height, 0) - sun * (height / sun.Y);

    [Fact]
    public void A_lit_style_gives_back_its_own_colour_as_the_sheet()
    {
        var colour = Color.FromRgb(0xDD, 0xBE, 0x92);
        var style = new SlabStyle(SlabStyle.Lit(colour, new Vec3(0.35, 0.85, 0.45)), Colors.Black, LineWidth.Px(1), Gold, Gold, LineWidth.Px(1));
        var sheet = style.SheetColour();
        Assert.True(Math.Abs(sheet.R - colour.R) <= 4 && Math.Abs(sheet.G - colour.G) <= 4 && Math.Abs(sheet.B - colour.B) <= 4, $"got {sheet}");
        Assert.Equal(Colors.Teal, (style with { Sheet = Colors.Teal }).SheetColour());
    }

    [Fact]
    public void The_sun_is_above_and_over_the_viewers_left_shoulder_however_its_turned()
    {
        foreach (var (yaw, pitch) in new[] { (0.0, 30.0), (120.0, 10.0), (250.0, 85.0), (40.0, -10.0) })
        {
            var cam = Camera3D.YUp(yaw, pitch);
            var sun = Model3DPainter.SunFor(cam);
            Assert.Equal(1, sun.Length, 6);
            Assert.True(sun.Y > 0.6, "high up");
            Assert.True(sun.Dot(cam.Right) < -0.2, "to the left");
            Assert.True(new Vec3(sun.X, 0, sun.Z).Dot(new Vec3(cam.Toward.X, 0, cam.Toward.Z)) > 0, "on the viewer's side");
        }
        // A model with Z up is lit from above it too.
        Assert.True(Model3DPainter.SunFor(Camera3D.ZUp(30, 30)).Z > 0.6);
    }

    [Fact]
    public void A_piece_casts_its_shadow_on_the_floor()
    {
        var cam = Camera3D.YUp(30, 60);
        var sun = Model3DPainter.SunFor(cam);
        var painter = new Model3DPainter();
        var pic = painter.Picture(FloorAndPillar(), cam, W, H, Bg, realistic: true)!;
        var inShadow = ShadowOf(sun, 24);
        // The same distance from the pillar, on the sunny side.
        var flat = new Vec3(inShadow.X, 0, inShadow.Z);
        var sunny = flat * -1;
        double dark = Brightness(pic, painter, inShadow), light = Brightness(pic, painter, sunny);
        Assert.True(dark < light * 0.7, $"in shadow {dark:0}, in the sun {light:0}");
        // Plainly, there are no shadows: both are the floor's own colour.
        var plain = painter.Picture(FloorAndPillar(), cam, W, H, Bg)!;
        Assert.Equal(Brightness(plain, painter, sunny), Brightness(plain, painter, inShadow), 0);
    }

    [Fact]
    public void A_model_with_nothing_under_it_casts_its_shadow_on_the_table()
    {
        var cam = Camera3D.YUp(30, 60);
        var sun = Model3DPainter.SunFor(cam);
        var painter = new Model3DPainter();
        // Drawn small enough to see the table round it.
        var pic = painter.Picture(FloorAndPillar(floor: false), cam, W, H, Bg, framing: new Framing(3, new Vec3(0, 10, 0)), realistic: true)!;
        var inShadow = ShadowOf(sun, 24);
        var flat = new Vec3(inShadow.X, 0, inShadow.Z);
        Assert.True(Brightness(pic, painter, inShadow) < Bg.R - 40, "the shadow darkens the table");
        // Well away from the pillar, on the sunny side, the table is the view's own colour.
        Assert.Equal(Bg.R, Brightness(pic, painter, flat * -1.6), 2);
    }

    [Fact]
    public void Whatever_is_nearest_still_shows()
    {
        // A small red card standing at the back of a long blue floor (as in the plain look's test).
        var scene = new Scene3D();
        scene.AddSlab(Rect(-50, -50, 50, 50), new Frame3(new(0, 0, 0), new(1, 0, 0), new(0, 0, -1)), -1, 0, Plain(Blue));
        scene.AddSlab(Rect(-5, 0, 5, 20), new Frame3(new(0, 0, -40), new(1, 0, 0), new(0, 1, 0)), -0.5, 0.5, Plain(Red));
        var painter = new Model3DPainter();
        var pic = painter.Picture(scene, Camera3D.YUp(0, 30), W, H, Bg, realistic: true)!;
        var card = Colour(pic, painter, new(0, 10, -39.5));
        var floor = Colour(pic, painter, new(20, 0, 20));
        Assert.True(card.R > card.B * 2, $"card {card}");
        Assert.True(floor.B > floor.R * 2, $"floor {floor}");
    }

    [Fact]
    public void Turning_it_quickly_draws_it_the_same_way_more_roughly()
    {
        var cam = Camera3D.YUp(30, 60);
        var painter = new Model3DPainter();
        var fine = painter.Picture(FloorAndPillar(), cam, W, H, Bg, realistic: true)!;
        var p = new Vec3(60, 0, 60);
        double want = Brightness(fine, painter, p);
        var rough = painter.Picture(FloorAndPillar(), cam, W, H, Bg, draft: true, realistic: true)!;
        Assert.Equal(want, Brightness(rough, painter, p), 0.15 * want);
    }
}
