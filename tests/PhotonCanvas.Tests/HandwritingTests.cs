using System.Xml.Linq;
using PhotonCanvas.Addon.Handwriting;
using PhotonCanvas.Core;
using PhotonCanvas.Core.Camera;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc;
using SkiaSharp;
using Xunit;

namespace PhotonCanvas.Tests;

/// <summary>The handwriting add-on: reading a filled-in sheet from a photo, and writing with the font.</summary>
public class HandwritingTests
{
    const double PxPerMm = 8, Margin = 15;

    static int Index(string ch) => Array.IndexOf(FontSheet.Characters, ch);

    /// <summary>A filled-in sheet as a flat picture: a few letters written with a 0.6 mm pen.</summary>
    static SKBitmap FilledSheet()
    {
        var bmp = FontSheet.Picture(PxPerMm, Margin);
        using var c = new SKCanvas(bmp);
        c.Scale((float)PxPerMm);
        c.Translate((float)Margin, (float)Margin);
        using var pen = new SKPaint { Color = new SKColor(20, 20, 30), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 0.6f, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
        using var dot = new SKPaint { Color = new SKColor(20, 20, 30), IsAntialias = true, Style = SKPaintStyle.Fill };
        void Lines(string ch, params (double X, double Y)[][] strokes)
        {
            var b = FontSheet.Box(Index(ch));
            foreach (var s in strokes)
            {
                using var p = new SKPath();
                p.MoveTo((float)(b.MinX + s[0].X), (float)(b.MinY + s[0].Y));
                foreach (var q in s.Skip(1)) p.LineTo((float)(b.MinX + q.X), (float)(b.MinY + q.Y));
                c.DrawPath(p, pen);
            }
        }
        void Dot(string ch, double x, double y, double r)
        {
            var b = FontSheet.Box(Index(ch));
            c.DrawCircle((float)(b.MinX + x), (float)(b.MinY + y), (float)r, dot);
        }
        Lines("A", [(3, 16), (8, 4), (13, 16)], [(5.3, 11), (10.7, 11)]);
        Lines("l", [(8, 4), (8, 16)]);
        Lines("i", [(8, 9.5), (8, 16)]);
        Dot("i", 8, 6.6, 0.55);
        Dot(".", 7, 15.3, 0.6);
        var b = FontSheet.Box(Index("o"));
        c.DrawCircle((float)(b.MinX + 8), (float)(b.MinY + 12.4), 3.4f, pen);
        return bmp;
    }

    /// <summary>The flat picture as a phone would see it: turned a quarter, in perspective, on a grey table, lit unevenly.</summary>
    static RasterImage Photo(SKBitmap flat) =>
        // Turned clockwise a quarter (the sheet's top is on the right), seen at an angle.
        Photo(flat, [new Vec2(1850, 140), new Vec2(1790, 1480), new Vec2(170, 1410), new Vec2(260, 80)]);

    static RasterImage Photo(SKBitmap flat, Vec2[] to)
    {
        const int W = 2000, H = 1600;
        var from = new[] { new Vec2(0, 0), new Vec2(flat.Width, 0), new Vec2(flat.Width, flat.Height), new Vec2(0, flat.Height) };
        var h = Homography.Fit([.. from.Zip(to)])!.Value;
        var m = new SKMatrix { ScaleX = (float)h.H11, SkewX = (float)h.H12, TransX = (float)h.H13, SkewY = (float)h.H21, ScaleY = (float)h.H22, TransY = (float)h.H23, Persp0 = (float)h.H31, Persp1 = (float)h.H32, Persp2 = 1 };
        using var bmp = new SKBitmap(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var c = new SKCanvas(bmp))
        {
            c.Clear(new SKColor(110, 104, 98));
            c.Save();
            c.SetMatrix(m);
            using var paint = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true };
            c.DrawBitmap(flat, 0, 0, paint);
            c.Restore();
            // Darker towards one side, as under a lamp.
            using var shade = new SKPaint
            {
                Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(W, 0), [new SKColor(0, 0, 0, 0), new SKColor(0, 0, 0, 90)], SKShaderTileMode.Clamp),
            };
            c.DrawRect(0, 0, W, H, shade);
        }
        var rgba = bmp.Bytes;
        return new RasterImage(W, H, rgba);
    }

    static double Length(HandGlyph g) => g.Strokes.Sum(s =>
    {
        double l = 0;
        for (int i = 1; i < s.Points.Count; i++) l += s.Points[i - 1].Dist(s.Points[i]);
        if (s.Closed) l += s.Points[^1].Dist(s.Points[0]);
        return l;
    });

    [Fact]
    public void ReadsTheLettersFromAPhotoTakenAtAnAngle()
    {
        using var flat = FilledSheet();
        var reading = SheetReader.Read(Photo(flat));
        Assert.Null(reading.Problem);
        Assert.Equal(FontSheet.Characters.Length, reading.Boxes.Count);
        Assert.Equal(5, reading.Written);
        Assert.Null(reading.Boxes[Index("B")].Glyph);

        var a = reading.Boxes[Index("A")].Glyph!;
        // Two legs (12.6 mm each) and a bar (5.4 mm), capitals 12 mm tall.
        Assert.InRange(Length(a), 30.6 / 12 * 0.88, 30.6 / 12 * 1.12);
        Assert.InRange(a.Advance, 10.0 / 12 - 0.08, 10.0 / 12 + 0.08);
        Assert.InRange(a.Strokes.Count, 2, 3);
        Assert.InRange(a.Bounds.MinY, -1.07, -0.93);
        Assert.InRange(a.Bounds.MaxY, -0.05, 0.05);

        var l = reading.Boxes[Index("l")].Glyph!;
        Assert.Single(l.Strokes);
        Assert.InRange(l.Bounds.Height, 0.9, 1.08);

        var o = reading.Boxes[Index("o")].Glyph!;
        Assert.Single(o.Strokes);
        Assert.True(o.Strokes[0].Closed);
        Assert.InRange(Length(o), 2 * Math.PI * 3.4 / 12 * 0.88, 2 * Math.PI * 3.4 / 12 * 1.1);

        var i = reading.Boxes[Index("i")].Glyph!;
        Assert.Equal(2, i.Strokes.Count);
        Assert.Contains(i.Strokes, s => s.Closed && Rect2.Bounds(s.Points).Width < 0.12); // the dot

        var stop = reading.Boxes[Index(".")].Glyph!;
        Assert.Single(stop.Strokes);
        Assert.True(stop.Strokes[0].Closed);
        Assert.InRange(stop.Bounds.MaxY, -0.12, 0.05); // on the line
    }

    [Fact]
    public void ReadsASheetPhotographedFromALowAngle()
    {
        // The top edge further away: the top squares come out much smaller than the bottom ones.
        using var flat = FilledSheet();
        var reading = SheetReader.Read(Photo(flat, [new Vec2(450, 160), new Vec2(1600, 190), new Vec2(1900, 1540), new Vec2(80, 1480)]));
        Assert.Null(reading.Problem);
        Assert.Equal(5, reading.Written);
        Assert.InRange(reading.Boxes[Index("A")].Glyph!.Advance, 10.0 / 12 - 0.1, 10.0 / 12 + 0.1);
    }

    [Fact]
    public void SaysWhenTheSheetIsNotInThePhoto()
    {
        var blank = new byte[800 * 600 * 4];
        Array.Fill(blank, (byte)200);
        var reading = SheetReader.Read(new RasterImage(800, 600, blank));
        Assert.NotNull(reading.Problem);
        Assert.Empty(reading.Boxes);
    }

    static HandFont TestFont()
    {
        HandGlyph Bar(double w) => new(w, [new HandStroke([new Vec2(0, 0), new Vec2(w, -1)], false)]);
        var g = new Dictionary<string, List<HandGlyph>>(StringComparer.Ordinal)
        {
            ["H"] = [Bar(0.7)],
            ["i"] = [Bar(0.1), Bar(0.12)],
            ["e"] = [Bar(0.5)],
            ["."] = [new HandGlyph(0.05, [new HandStroke([new Vec2(0, 0), new Vec2(0.05, 0), new Vec2(0.05, -0.05)], true)])],
        };
        return new HandFont { Name = "Test hand", Glyphs = g };
    }

    [Fact]
    public void WritesWordsAtTheChosenSize()
    {
        var font = TestFont();
        var w = HandLayout.Write(font, "Hi", new HandStyle { SizeMm = 10, LetterSpacing = 0.1, Variation = 0 });
        Assert.Equal(2, w.Letters);
        Assert.Empty(w.Missing);
        var b = w.Geometry.Bounds();
        Assert.Equal(0, b.MinX, 3);
        Assert.Equal(0, b.MinY, 3);
        Assert.Equal(10, b.Height, 2); // capital height
        Assert.Equal((0.7 + 0.1 + 0.1) * 10, b.Width, 2);
        Assert.All(w.Geometry.Subpaths, s => Assert.False(s.Closed));
    }

    [Fact]
    public void LettersMissingFromTheFontAreReportedAndStandInsUsed()
    {
        var w = HandLayout.Write(TestFont(), "Hé hq …", new HandStyle { Variation = 0 });
        Assert.Contains("q", w.Missing);
        Assert.Contains("é", w.StandIns);
        Assert.Contains("h", w.StandIns); // written with H
        Assert.DoesNotContain("…", w.Missing); // written as three full stops
    }

    [Fact]
    public void RepeatedLettersUseTheirOtherVersionsAndVariationRepeats()
    {
        var font = TestFont();
        var style = new HandStyle { Variation = 0, LetterSpacing = 0 };
        var w = HandLayout.Write(font, "ii", style);
        var b = w.Geometry.Bounds();
        // One of each width: 0.1 + 0.12 (never the same version twice in a row).
        Assert.Equal(0.22 * style.SizeMm, b.Width, 2);
        var wobbly = style with { Variation = 1, Seed = 7 };
        var a1 = HandLayout.Write(font, "Hie Hie", wobbly).Geometry.Bounds();
        var a2 = HandLayout.Write(font, "Hie Hie", wobbly).Geometry.Bounds();
        Assert.Equal(a1, a2);
    }

    [Fact]
    public void FontFileRoundTripsAndMerges()
    {
        var font = TestFont();
        var back = HandFont.FromJson(font.ToJson());
        Assert.Equal(font.Name, back.Name);
        Assert.Equal(font.Count, back.Count);
        Assert.True(back.Glyphs["."][0].Strokes[0].Closed);
        var more = back.With([("H", new HandGlyph(0.6, [new HandStroke([new Vec2(0, 0), new Vec2(0.6, 0)], false)]))], asExtraVersions: true);
        Assert.Equal(2, more.Glyphs["H"].Count);
        var replaced = back.With([("H", new HandGlyph(0.6, [new HandStroke([new Vec2(0, 0), new Vec2(0.6, 0)], false)]))], asExtraVersions: false);
        Assert.Single(replaced.Glyphs["H"]);
        Assert.Throws<FormatException>(() => HandFont.FromJson("{\"kind\":\"something else\"}"));
    }

    [Fact]
    public void SvgFontHasAGlyphPerCharacter()
    {
        var svg = XDocument.Parse(TestFont().ToSvgFont());
        XNamespace ns = "http://www.w3.org/2000/svg";
        var glyphs = svg.Descendants(ns + "glyph").ToList();
        Assert.Equal(5, glyphs.Count); // four characters and the space
        Assert.Contains(glyphs, g => (string?)g.Attribute("unicode") == "H" && ((string?)g.Attribute("d"))!.StartsWith("M "));
    }

    [Fact]
    public void SheetFitsA4AndLetterAndHasABoxForEveryCharacter()
    {
        foreach (var p in new[] { Paper.A4, Paper.Letter })
        {
            var (w, h) = FontSheet.PaperSize(p);
            Assert.True(FontSheet.Width <= w - 10 && FontSheet.Height <= h - 10);
        }
        var last = FontSheet.Box(FontSheet.Characters.Length - 1);
        Assert.True(last.MaxY < FontSheet.Height - FontSheet.Marker - 12, "the boxes leave room for the bottom squares and the notes");
        Assert.True(last.MaxX <= FontSheet.Width - 5);
        Assert.Equal(FontSheet.Characters.Length, FontSheet.Characters.Distinct().Count());
        var pdf = FontSheet.Pdf(Paper.A4, "Test");
        Assert.True(pdf.Length > 1000);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
    }

    [Fact]
    public void LinesMeetingStraightOnAreJoined()
    {
        // A cross: four arms meeting in the middle become two lines.
        var lines = new List<List<Vec2>>
        {
            new() { new(0, 10), new(10, 10) }, new() { new(10, 10), new(20, 10) },
            new() { new(10, 0), new(10, 10) }, new() { new(10, 10), new(10, 20) },
        };
        var joined = SheetReader.Join(lines, 2, 70);
        Assert.Equal(2, joined.Count);
        Assert.All(joined, l => Assert.Equal(20, l[0].Dist(l[^1]), 3));
    }
}
