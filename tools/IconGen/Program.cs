using SkiaSharp;

// PhotonCanvas app icon: a laser beam lands on a dark tile, leaving a glowing
// "burned" stroke — light drawing on a canvas. Drawn in a 256-unit box and
// rendered at every size Windows asks for, then packed into an .ico.
//
//   dotnet run --project tools/IconGen [output folder]

var outDir = args.Length > 0 ? args[0] : Path.Combine(FindRepo(), "src", "PhotonCanvas.App", "Assets");
Directory.CreateDirectory(outDir);

int[] sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
var pngs = sizes.Select(s => (Size: s, Png: Render(s))).ToList();
File.WriteAllBytes(Path.Combine(outDir, "photoncanvas.ico"), Ico(pngs));
File.WriteAllBytes(Path.Combine(outDir, "photoncanvas-512.png"), Render(512));
Console.WriteLine($"Wrote photoncanvas.ico ({string.Join(", ", sizes)} px) and photoncanvas-512.png to {outDir}");

static byte[] Render(int size)
{
    using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul));
    var c = surface.Canvas;
    c.Clear(SKColors.Transparent);
    c.Scale(size / 256f);
    bool tiny = size <= 24; // fewer, bolder details when there are few pixels

    // Tile: deep navy, lit from the top left.
    var tile = new SKRoundRect(new SKRect(8, 8, 248, 248), 52, 52);
    using (var bg = new SKPaint { IsAntialias = true })
    {
        bg.Shader = SKShader.CreateLinearGradient(new SKPoint(8, 8), new SKPoint(248, 248),
            [new SKColor(0x1E, 0x2E, 0x52), new SKColor(0x0B, 0x12, 0x22)], SKShaderTileMode.Clamp);
        c.DrawRoundRect(tile, bg);
    }
    // A faint edge so the tile stands out on dark taskbars (stronger when small).
    using (var rim = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = tiny ? 10 : 3, Color = new SKColor(0x9F, 0xC0, 0xFF, (byte)(tiny ? 70 : 32)) })
        c.DrawRoundRect(tile, rim);

    c.Save();
    c.ClipRoundRect(tile, antialias: true);

    var spot = new SKPoint(176, 132);

    // The burned stroke: an S-curve trailing back from the hot spot, cooling from yellow to deep orange.
    using var stroke = new SKPath();
    stroke.MoveTo(42, 184);
    stroke.CubicTo(82, 100, 124, 222, spot.X, spot.Y);
    var burn = SKShader.CreateLinearGradient(new SKPoint(42, 184), spot,
        [new SKColor(0xE2, 0x4A, 0x0E), new SKColor(0xFF, 0x8A, 0x1E), new SKColor(0xFF, 0xD8, 0x6A)], [0, 0.6f, 1], SKShaderTileMode.Clamp);
    using (var glow = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeWidth = tiny ? 34 : 30, Shader = burn,
               MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 10), Color = SKColors.White.WithAlpha(150) })
        c.DrawPath(stroke, glow);
    using (var line = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeWidth = tiny ? 24 : 16, Shader = burn })
        c.DrawPath(stroke, line);

    // The beam: from the top edge down to the spot, narrowing, cyan to white.
    using var beam = new SKPath();
    beam.MoveTo(spot.X - (tiny ? 22 : 16), 0);
    beam.LineTo(spot.X + (tiny ? 22 : 16), 0);
    beam.LineTo(spot.X + 3, spot.Y);
    beam.LineTo(spot.X - 3, spot.Y);
    beam.Close();
    var beamShader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, spot.Y),
        [new SKColor(0x4F, 0xC3, 0xFF, 40), new SKColor(0x8F, 0xE4, 0xFF, 200), SKColors.White], [0, 0.55f, 1], SKShaderTileMode.Clamp);
    using (var beamGlow = new SKPaint { IsAntialias = true, Shader = beamShader, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 9) })
        c.DrawPath(beam, beamGlow);
    using (var beamCore = new SKPaint { IsAntialias = true, Shader = beamShader })
        c.DrawPath(beam, beamCore);

    // Hot spot where the light lands.
    using (var hot = new SKPaint { IsAntialias = true })
    {
        hot.Shader = SKShader.CreateRadialGradient(spot, tiny ? 40 : 34,
            [SKColors.White, new SKColor(0xFF, 0xF2, 0xB0, 230), new SKColor(0xFF, 0xA0, 0x30, 90), SKColors.Transparent], [0, 0.25f, 0.55f, 1], SKShaderTileMode.Clamp);
        c.DrawCircle(spot, tiny ? 40 : 34, hot);
    }
    c.Restore();

    using var img = surface.Snapshot();
    using var data = img.Encode(SKEncodedImageFormat.Png, 100);
    return data.ToArray();
}

// .ico with PNG-compressed images (supported by Windows Vista and later).
static byte[] Ico(List<(int Size, byte[] Png)> images)
{
    using var ms = new MemoryStream();
    using var w = new BinaryWriter(ms);
    w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)images.Count);
    int offset = 6 + 16 * images.Count;
    foreach (var (size, png) in images)
    {
        w.Write((byte)(size >= 256 ? 0 : size)); w.Write((byte)(size >= 256 ? 0 : size));
        w.Write((byte)0); w.Write((byte)0);
        w.Write((ushort)1); w.Write((ushort)32);
        w.Write(png.Length); w.Write(offset);
        offset += png.Length;
    }
    foreach (var (_, png) in images) w.Write(png);
    return ms.ToArray();
}

static string FindRepo()
{
    for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        if (File.Exists(Path.Combine(d.FullName, "PhotonCanvas.slnx"))) return d.FullName;
    throw new InvalidOperationException("Run from inside the PhotonCanvas repository, or pass an output folder.");
}
