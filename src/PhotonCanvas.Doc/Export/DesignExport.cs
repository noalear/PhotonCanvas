using System.Globalization;
using System.Text;
using PhotonCanvas.Core;
using SkiaSharp;

namespace PhotonCanvas.Doc.Export;

/// <summary>
/// The design (not the job) for other programs: SVG with pictures and layer colours, DXF
/// outlines with one DXF layer per layer, or a PNG picture at a chosen resolution.
/// Hidden layers are left out.
/// </summary>
public static class DesignExport
{
    static readonly CultureInfo C = CultureInfo.InvariantCulture;

    static string F(double v) => v.ToString("0.####", C);

    static IEnumerable<(Layer Layer, Shape Shape)> Visible(DocState s)
    {
        var layers = s.Layers.ToDictionary(l => l.Id);
        foreach (var sh in s.Shapes)
            if (layers.TryGetValue(sh.LayerId, out var l) && l.Visible) yield return (l, sh);
    }

    static string Hex(uint c) => $"#{c & 0xFFFFFF:x6}";

    /// <summary>SVG in millimetres: one group per layer (named after it), fills for engrave layers, pictures embedded.</summary>
    public static string Svg(DocState s)
    {
        var all = Visible(s).ToList();
        var box = all.Select(x => x.Shape.Bounds).Aggregate(Rect2.Empty, (a, b) => a.Union(b));
        if (box.IsEmpty) box = new Rect2(0, 0, 10, 10);
        var sb = new StringBuilder();
        sb.Append($"<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" width=\"{F(box.Width)}mm\" height=\"{F(box.Height)}mm\" viewBox=\"{F(box.MinX)} {F(box.MinY)} {F(box.Width)} {F(box.Height)}\">\n");
        foreach (var g in all.GroupBy(x => x.Layer.Id))
        {
            var l = g.First().Layer;
            sb.Append($"  <g id=\"{Escape(l.Name)}\">\n");
            foreach (var (_, sh) in g)
            {
                switch (sh)
                {
                    case PathShape p:
                        bool fill = l.Mode == LayerMode.Fill;
                        sb.Append($"    <path d=\"{p.World.ToSvgPathData()}\" fill=\"{(fill ? Hex(l.Color) : "none")}\" fill-rule=\"evenodd\" stroke=\"{Hex(l.Color)}\" stroke-width=\"0.1\"/>\n");
                        break;
                    case ImageShape im:
                        var t = im.Transform;
                        sb.Append($"    <image transform=\"matrix({F(t.A)} {F(t.B)} {F(t.C)} {F(t.D)} {F(t.E)} {F(t.F)})\" width=\"{F(im.SizeMm.X)}\" height=\"{F(im.SizeMm.Y)}\" preserveAspectRatio=\"none\" xlink:href=\"data:image/png;base64,{Convert.ToBase64String(im.Source.Pixels.EncodePng())}\"/>\n");
                        break;
                }
            }
            sb.Append("  </g>\n");
        }
        sb.Append("</svg>\n");
        return sb.ToString();
    }

    static string Escape(string s) => s.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;");

    /// <summary>DXF (R12, millimetres): every outline as a polyline on a DXF layer named after its layer. Pictures are left out.</summary>
    public static string Dxf(DocState s, double tol = 0.01)
    {
        var sb = new StringBuilder();
        void G(int code, string value) => sb.Append(code).Append('\n').Append(value).Append('\n');
        G(0, "SECTION"); G(2, "HEADER");
        G(9, "$INSUNITS"); G(70, "4"); // millimetres
        G(0, "ENDSEC");
        var all = Visible(s).ToList();
        G(0, "SECTION"); G(2, "TABLES"); G(0, "TABLE"); G(2, "LAYER");
        foreach (var l in all.Select(x => x.Layer).DistinctBy(l => l.Id))
        {
            G(0, "LAYER"); G(2, DxfName(l.Name)); G(70, "0"); G(62, "7"); G(6, "CONTINUOUS");
        }
        G(0, "ENDTAB"); G(0, "ENDSEC");
        G(0, "SECTION"); G(2, "ENTITIES");
        foreach (var (l, sh) in all)
        {
            if (sh is not PathShape p) continue;
            foreach (var f in p.World.Flatten(tol))
            {
                if (f.Points.Count < 2) continue;
                G(0, "POLYLINE"); G(8, DxfName(l.Name)); G(66, "1"); G(70, f.Closed ? "1" : "0");
                foreach (var v in f.Points)
                {
                    // DXF's Y points up: the design is mirrored so it reads the right way round.
                    G(0, "VERTEX"); G(8, DxfName(l.Name)); G(10, F(v.X)); G(20, F(-v.Y)); G(30, "0");
                }
                G(0, "SEQEND");
            }
        }
        G(0, "ENDSEC"); G(0, "EOF");
        return sb.ToString();
    }

    static string DxfName(string n)
    {
        var clean = new string(n.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray());
        return clean.Length == 0 ? "0" : clean;
    }

    /// <summary>The design as a picture: shapes in their layer colours (filled for engrave layers) and pictures, on white.</summary>
    public static byte[] Png(DocState s, double dpi = 300) => Render(s, dpi, false);

    /// <summary>
    /// The shapes as a black-and-white picture (closed shapes filled, open lines drawn), at
    /// <paramref name="dpi"/>, covering <paramref name="area"/> (mm): for turning shapes into a picture.
    /// </summary>
    public static byte[] ShapesToPng(IReadOnlyList<PathShape> shapes, Rect2 area, double dpi, double lineMm = 0.2)
    {
        double k = dpi / 25.4;
        int w = Math.Max(1, (int)Math.Ceiling(area.Width * k)), h = Math.Max(1, (int)Math.Ceiling(area.Height * k));
        using var surface = SKSurface.Create(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
        var c = surface.Canvas;
        c.Clear(SKColors.Transparent);
        c.Scale((float)k);
        c.Translate((float)-area.MinX, (float)-area.MinY);
        using var fill = new SKPaint { Color = SKColors.Black, IsAntialias = true, Style = SKPaintStyle.Fill };
        using var stroke = new SKPaint { Color = SKColors.Black, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = (float)lineMm };
        foreach (var p in shapes)
        {
            using var path = ToSkia(p.World);
            if (p.World.Subpaths.Any(sp => sp.Closed)) c.DrawPath(path, fill);
            else c.DrawPath(path, stroke);
        }
        using var img = surface.Snapshot();
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    static byte[] Render(DocState s, double dpi, bool mono)
    {
        var all = Visible(s).ToList();
        var box = all.Select(x => x.Shape.Bounds).Aggregate(Rect2.Empty, (a, b) => a.Union(b));
        if (box.IsEmpty) box = new Rect2(0, 0, 10, 10);
        double k = dpi / 25.4;
        int w = Math.Clamp((int)Math.Ceiling(box.Width * k), 1, 20000), h = Math.Clamp((int)Math.Ceiling(box.Height * k), 1, 20000);
        using var surface = SKSurface.Create(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
        var c = surface.Canvas;
        c.Clear(SKColors.White);
        c.Scale((float)k);
        c.Translate((float)-box.MinX, (float)-box.MinY);
        foreach (var (l, sh) in all)
        {
            var colour = mono ? SKColors.Black : new SKColor((byte)(l.Color >> 16), (byte)(l.Color >> 8), (byte)l.Color);
            switch (sh)
            {
                case PathShape p:
                    using (var path = ToSkia(p.World))
                    using (var paint = new SKPaint { Color = colour, IsAntialias = true, Style = l.Mode == LayerMode.Fill ? SKPaintStyle.Fill : SKPaintStyle.Stroke, StrokeWidth = (float)(1.5 / k) })
                        c.DrawPath(path, paint);
                    break;
                case ImageShape im:
                    using (var bmp = SKBitmap.Decode(im.Source.Pixels.EncodePng()))
                    {
                        if (bmp is null) break;
                        c.Save();
                        var t = im.Transform;
                        var m = new SKMatrix((float)t.A, (float)t.C, (float)t.E, (float)t.B, (float)t.D, (float)t.F, 0, 0, 1);
                        c.Concat(ref m);
                        c.DrawBitmap(bmp, new SKRect(0, 0, (float)im.SizeMm.X, (float)im.SizeMm.Y));
                        c.Restore();
                    }
                    break;
            }
        }
        using var img = surface.Snapshot();
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    static SKPath ToSkia(PathGeometry g)
    {
        var p = new SKPath { FillType = SKPathFillType.EvenOdd };
        foreach (var sp in g.Subpaths.Where(x => !x.Segs.IsEmpty))
        {
            p.MoveTo((float)sp.Start.X, (float)sp.Start.Y);
            foreach (var seg in sp.Segs)
            {
                if (seg.Kind == SegKind.Line) p.LineTo((float)seg.To.X, (float)seg.To.Y);
                else p.CubicTo((float)seg.C1.X, (float)seg.C1.Y, (float)seg.C2.X, (float)seg.C2.Y, (float)seg.To.X, (float)seg.To.Y);
            }
            if (sp.Closed) p.Close();
        }
        return p;
    }
}
