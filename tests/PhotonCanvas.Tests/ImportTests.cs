using System.IO.Compression;
using System.Text;
using SkiaSharp;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Import;

namespace PhotonCanvas.Tests;

public class ImportTests
{
    const double Eps = 1e-6;

    static void Near(double expected, double actual, double tol = Eps) =>
        Assert.True(Math.Abs(expected - actual) <= tol, $"expected {expected}, got {actual}");

    static void Near(Vec2 expected, Vec2 actual, double tol = Eps) =>
        Assert.True(expected.Dist(actual) <= tol, $"expected {expected}, got {actual}");

    /// <summary>Point on a cubic segment at parameter t.</summary>
    static Vec2 CubicAt(Vec2 p0, Seg s, double t)
    {
        double u = 1 - t;
        return p0 * (u * u * u) + s.C1 * (3 * u * u * t) + s.C2 * (3 * u * t * t) + s.To * (t * t * t);
    }

    // ---- DXF ----------------------------------------------------------------

    /// <summary>Builds DXF text from group-code/value pairs.</summary>
    static string Tags(params object[] pairs) =>
        string.Join("\n", pairs.Select(p => Convert.ToString(p, System.Globalization.CultureInfo.InvariantCulture))) + "\n";

    static string Dxf(string entities, int? insunits = 4, string tables = "", string blocks = "")
    {
        var sb = new StringBuilder();
        sb.Append(Tags(0, "SECTION", 2, "HEADER", 9, "$ACADVER", 1, "AC1027"));
        if (insunits is { } u) sb.Append(Tags(9, "$INSUNITS", 70, u));
        sb.Append(Tags(0, "ENDSEC"));
        if (tables.Length > 0) sb.Append(Tags(0, "SECTION", 2, "TABLES", 0, "TABLE", 2, "LAYER")).Append(tables).Append(Tags(0, "ENDTAB", 0, "ENDSEC"));
        if (blocks.Length > 0) sb.Append(Tags(0, "SECTION", 2, "BLOCKS")).Append(blocks).Append(Tags(0, "ENDSEC"));
        sb.Append(Tags(0, "SECTION", 2, "ENTITIES")).Append(entities).Append(Tags(0, "ENDSEC", 0, "EOF"));
        return sb.ToString();
    }

    static string Line(double x1, double y1, double x2, double y2, string layer = "0") =>
        Tags(0, "LINE", 8, layer, 10, x1, 20, y1, 30, 0, 11, x2, 21, y2, 31, 0);

    [Fact]
    public void Dxf_line_is_flipped_upright_and_moved_to_origin()
    {
        var r = DxfImport.ParseString(Dxf(Line(5, 5, 15, 25) + Line(5, 5, 15, 5)));
        Assert.Equal(2, r.Paths.Count);
        Assert.Empty(r.Warnings);
        var a = r.Paths[0].Geometry.Subpaths[0];
        // DXF (5,5) is the bottom-left of a 10 × 20 drawing: on screen it is (0, 20).
        Near(new Vec2(0, 20), a.Start);
        Near(new Vec2(10, 0), a.End);
        var b = r.Bounds;
        Near(0, b.MinX); Near(0, b.MinY); Near(10, b.Width); Near(20, b.Height);
    }

    [Fact]
    public void Dxf_closed_lwpolyline_with_bulge_makes_correct_arc()
    {
        double bulge = Math.Tan(Math.PI / 8); // 90° counter-clockwise arc
        var e = Tags(0, "LWPOLYLINE", 8, "0", 90, 4, 70, 1,
            10, 0, 20, 0, 42, bulge,
            10, 10, 20, 0,
            10, 10, 20, 10,
            10, 0, 20, 10);
        var r = DxfImport.ParseString(Dxf(e));
        var sp = Assert.Single(Assert.Single(r.Paths).Geometry.Subpaths);
        Assert.True(sp.Closed);
        // The arc from (0,0) to (10,0) swings below the chord around centre (5,5), radius √50:
        // its middle is DXF (5, 5 − √50). The drawing spans y = 5 − √50 … 10, so on screen
        // (y flipped, top at 0) that middle sits at y = 10 − (5 − √50).
        double r50 = Math.Sqrt(50);
        var arc = sp.Segs[0];
        Assert.Equal(SegKind.Cubic, arc.Kind);
        Near(new Vec2(5, 10 - (5 - r50)), CubicAt(sp.Start, arc, 0.5), 1e-9);
        Near(new Vec2(0, 10), sp.Start); // DXF (0,0)
        Near(new Vec2(10, 10), arc.To);  // DXF (10,0)
        Near(10 + r50 - 5, r.Bounds.Height, 0.02);
    }

    [Fact]
    public void Dxf_circle_arc_and_ellipse()
    {
        // Circle r=10 at (50,50); a quarter arc and a quarter ellipse elsewhere.
        var circle = DxfImport.ParseString(Dxf(Tags(0, "CIRCLE", 8, "0", 10, 50, 20, 50, 40, 10)));
        var cb = circle.Bounds;
        Near(20, cb.Width, 0.02); Near(20, cb.Height, 0.02);
        Assert.True(circle.Paths[0].Geometry.Subpaths[0].Closed);

        var arc = DxfImport.ParseString(Dxf(Tags(0, "ARC", 8, "0", 10, 0, 20, 0, 40, 10, 50, 0, 51, 90)));
        var asp = arc.Paths[0].Geometry.Subpaths[0];
        Assert.False(asp.Closed);
        Near(new Vec2(10, 10), asp.Start); // DXF (10,0): bottom-right
        Near(new Vec2(0, 0), asp.End);     // DXF (0,10): top-left
        Near(new Vec2(10 * Math.Cos(Math.PI / 4), 10 - 10 * Math.Sin(Math.PI / 4)), CubicAt(asp.Start, asp.Segs[0], 0.5), 1e-9);

        var full = DxfImport.ParseString(Dxf(Tags(0, "ELLIPSE", 8, "0", 10, 0, 20, 0, 11, 20, 21, 0, 40, 0.5, 41, 0, 42, 2 * Math.PI)));
        Near(40, full.Bounds.Width, 0.02); Near(20, full.Bounds.Height, 0.02);
        Assert.True(full.Paths[0].Geometry.Subpaths[0].Closed);

        var part = DxfImport.ParseString(Dxf(Tags(0, "ELLIPSE", 8, "0", 10, 0, 20, 0, 11, 20, 21, 0, 40, 0.5, 41, 0, 42, Math.PI / 2)));
        var psp = part.Paths[0].Geometry.Subpaths[0];
        Assert.False(psp.Closed);
        Near(new Vec2(20, 10), psp.Start); // DXF (20,0)
        Near(new Vec2(0, 0), psp.End);     // DXF (0,10)
    }

    [Fact]
    public void Dxf_cubic_spline_becomes_exact_bezier()
    {
        var e = Tags(0, "SPLINE", 8, "0", 70, 8, 71, 3, 72, 8, 73, 4, 74, 0,
            40, 0, 40, 0, 40, 0, 40, 0, 40, 1, 40, 1, 40, 1, 40, 1,
            10, 0, 20, 0, 30, 0, 10, 0, 20, 10, 30, 0, 10, 10, 20, 10, 30, 0, 10, 10, 20, 0, 30, 0);
        var r = DxfImport.ParseString(Dxf(e));
        var sp = r.Paths[0].Geometry.Subpaths[0];
        var s = Assert.Single(sp.Segs);
        Assert.Equal(SegKind.Cubic, s.Kind);
        // The curve peaks at y = 7.5, so the flipped drawing has its top at DXF y = 7.5.
        Near(new Vec2(0, 7.5), sp.Start);
        Near(new Vec2(0, -2.5), s.C1);
        Near(new Vec2(10, -2.5), s.C2);
        Near(new Vec2(10, 7.5), s.To);
    }

    [Fact]
    public void Dxf_quadratic_spline_with_interior_knot_passes_through_de_boor_points()
    {
        // Clamped quadratic B-spline; at the interior knot the curve is at the midpoint of P1 and P2.
        var e = Tags(0, "SPLINE", 8, "0", 70, 8, 71, 2, 72, 7, 73, 4,
            40, 0, 40, 0, 40, 0, 40, 1, 40, 2, 40, 2, 40, 2,
            10, 0, 20, 0, 10, 10, 20, 0, 10, 10, 20, 10, 10, 0, 20, 10);
        var r = DxfImport.ParseString(Dxf(e));
        var sp = r.Paths[0].Geometry.Subpaths[0];
        Assert.Equal(2, sp.Segs.Length);
        Assert.All(sp.Segs, s => Assert.Equal(SegKind.Cubic, s.Kind));
        Near(new Vec2(0, 10), sp.Start);         // DXF (0,0)
        Near(new Vec2(10, 5), sp.Segs[0].To);    // DXF (10,5), the knot point
        Near(new Vec2(0, 0), sp.Segs[1].To);     // DXF (0,10)
    }

    [Fact]
    public void Dxf_rational_spline_quarter_circle_stays_on_the_circle()
    {
        double w = Math.Sqrt(0.5);
        var e = Tags(0, "SPLINE", 8, "0", 70, 12, 71, 2, 72, 6, 73, 3,
            40, 0, 40, 0, 40, 0, 40, 1, 40, 1, 40, 1,
            41, 1, 41, w, 41, 1,
            10, 10, 20, 0, 10, 10, 20, 10, 10, 0, 20, 10);
        var r = DxfImport.ParseString(Dxf(e));
        // Centre DXF (0,0) is the bottom-left of the 10 × 10 drawing → (0, 10) on screen.
        var pts = r.Paths[0].Geometry.Flatten(0.001)[0].Points;
        Assert.True(pts.Count > 5);
        Assert.All(pts, p => Near(10, p.Dist(new Vec2(0, 10)), 0.01));
    }

    [Fact]
    public void Dxf_insert_applies_block_base_scale_rotation_and_byblock_colour()
    {
        var blocks = Tags(0, "BLOCK", 8, "0", 2, "B", 70, 0, 10, 1, 20, 0, 30, 0)
                     + Tags(0, "LINE", 8, "0", 62, 0, 10, 1, 20, 0, 30, 0, 11, 11, 21, 0, 31, 0)
                     + Tags(0, "ENDBLK", 8, "0");
        var ents = Line(0, 0, 1, 0, "Ref")
                   + Tags(0, "INSERT", 8, "Parts", 62, 1, 2, "B", 10, 100, 20, 100, 30, 0, 41, 2, 42, 2, 50, 90);
        var r = DxfImport.ParseString(Dxf(ents, blocks: blocks));
        Assert.Equal(2, r.Paths.Count);
        var ins = r.Paths[1];
        Assert.Equal("Parts", ins.Name);          // layer-0 block contents take the reference's layer
        Assert.Equal(0xFF0000u, ins.Color);        // BYBLOCK → the reference's colour (ACI 1 = red)
        // Block line (1,0)-(11,0) relative to base (1,0) → length 10, ×2, rotated 90° CCW, at (100,100):
        // DXF (100,100)-(100,120). The drawing spans y 0…120, so on screen that is (100,20)-(100,0).
        var sp = ins.Geometry.Subpaths[0];
        Near(new Vec2(100, 20), sp.Start, 1e-9);
        Near(new Vec2(100, 0), sp.End, 1e-9);
    }

    [Fact]
    public void Dxf_nested_and_self_referencing_blocks()
    {
        var blocks = Tags(0, "BLOCK", 2, "Inner", 10, 0, 20, 0) + Line(0, 0, 1, 0) + Tags(0, "ENDBLK")
                     + Tags(0, "BLOCK", 2, "Outer", 10, 0, 20, 0)
                     + Tags(0, "INSERT", 8, "0", 2, "Inner", 10, 5, 20, 0)
                     + Tags(0, "INSERT", 8, "0", 2, "Outer", 10, 50, 20, 0) // loops back to itself
                     + Tags(0, "ENDBLK");
        var r = DxfImport.ParseString(Dxf(Tags(0, "INSERT", 8, "0", 2, "Outer", 10, 0, 20, 0), blocks: blocks));
        Assert.Single(r.Paths);
        Assert.Contains(r.Warnings, w => w.Contains("contains itself"));
    }

    [Fact]
    public void Dxf_inches_are_converted_to_mm()
    {
        var r = DxfImport.ParseString(Dxf(Line(0, 0, 1, 0) + Line(0, 0, 0, 2), insunits: 1));
        Near(25.4, r.Bounds.Width); Near(50.8, r.Bounds.Height);
        Assert.Empty(r.Warnings);

        // Declared inches that make a 2.5 m drawing: imported as declared, but flagged.
        var big = DxfImport.ParseString(Dxf(Line(0, 0, 100, 0) + Line(0, 0, 0, 90), insunits: 1));
        Near(2540, big.Bounds.Width);
        Assert.Contains(big.Warnings, w => w.Contains("inches") && w.Contains("25.4"));
    }

    [Fact]
    public void Dxf_without_units_warns_only_when_size_is_implausible()
    {
        Assert.Empty(DxfImport.ParseString(Dxf(Line(0, 0, 100, 0), insunits: null)).Warnings);
        var tiny = DxfImport.ParseString(Dxf(Line(0, 0, 0.5, 0), insunits: null));
        Assert.Contains(tiny.Warnings, w => w.Contains("25.4"));
    }

    [Fact]
    public void Dxf_colours_come_from_entity_true_colour_aci_or_layer()
    {
        var tables = Tags(0, "LAYER", 2, "Cut", 70, 0, 62, 5) + Tags(0, "LAYER", 2, "Off", 70, 0, 62, -3)
                     + Tags(0, "LAYER", 2, "Engrave", 70, 0, 62, 7, 420, 0x00AB12);
        var ents = Line(0, 0, 1, 0, "Cut")                                              // BYLAYER → blue
                   + Tags(0, "LINE", 8, "Cut", 62, 3, 10, 0, 20, 0, 11, 1, 21, 1)         // ACI 3 → green
                   + Tags(0, "LINE", 8, "Cut", 420, 0x123456, 10, 0, 20, 0, 11, 1, 21, 2) // true colour
                   + Line(0, 0, 2, 0, "Engrave")                                          // layer true colour
                   + Tags(0, "LINE", 8, "Cut", 62, 7, 10, 0, 20, 0, 11, 2, 21, 2)         // ACI 7 → black
                   + Tags(0, "LINE", 8, "Cut", 62, 250, 10, 0, 20, 0, 11, 3, 21, 2)       // grey
                   + Tags(0, "LINE", 8, "Cut", 62, 10, 10, 0, 20, 0, 11, 3, 21, 3)        // ACI 10 → red
                   + Line(0, 0, 5, 5, "Off");                                             // hidden layer
        var r = DxfImport.ParseString(Dxf(ents, tables: tables));
        Assert.Equal([0x0000FFu, 0x00FF00u, 0x123456u, 0x00AB12u, 0x000000u, 0x333333u, 0xFF0000u], r.Paths.Select(p => p.Color!.Value));
        Assert.Equal("Cut", r.Paths[0].Name);
        Assert.Contains(r.Warnings, w => w.Contains("hidden"));
        Assert.Equal("Engrave", r.LayerNames[0x00AB12]);
    }

    [Fact]
    public void Dxf_text_and_hatch_are_reported_not_imported()
    {
        var ents = Line(0, 0, 10, 0) + Tags(0, "TEXT", 8, "0", 10, 0, 20, 0, 40, 5, 1, "Hello") + Tags(0, "HATCH", 8, "0", 2, "SOLID");
        var r = DxfImport.ParseString(Dxf(ents));
        Assert.Single(r.Paths);
        Assert.Contains(r.Warnings, w => w.StartsWith("Text was skipped"));
        Assert.Contains(r.Warnings, w => w.StartsWith("Hatch"));
    }

    [Fact]
    public void Dxf_old_style_polyline_with_vertices()
    {
        var e = Tags(0, "POLYLINE", 8, "0", 66, 1, 70, 1, 10, 0, 20, 0)
                + Tags(0, "VERTEX", 8, "0", 10, 0, 20, 0, 42, 1)
                + Tags(0, "VERTEX", 8, "0", 10, 10, 20, 0)
                + Tags(0, "SEQEND", 8, "0");
        var r = DxfImport.ParseString(Dxf(e));
        // A semicircle below the chord (bulge 1, counter-clockwise) closed by a straight edge.
        var sp = Assert.Single(r.Paths).Geometry.Subpaths[0];
        Assert.True(sp.Closed);
        Near(10, r.Bounds.Width, 0.02);
        Near(5, r.Bounds.Height, 0.02);
    }

    [Fact]
    public void Dxf_binary_and_garbage_are_rejected_clearly()
    {
        var bin = Encoding.ASCII.GetBytes("AutoCAD Binary DXF\r\n\u001a\0rest");
        var ex = Assert.Throws<FormatException>(() => DxfImport.Parse(bin));
        Assert.Contains("binary", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<FormatException>(() => DxfImport.ParseString("hello\nworld\n"));
    }

    [Fact]
    public void Aci_table_spot_checks()
    {
        Assert.Equal(0xFF0000u, DxfImport.AciToRgb(1));
        Assert.Equal(0xFFFF00u, DxfImport.AciToRgb(2));
        Assert.Equal(0x808080u, DxfImport.AciToRgb(8));
        Assert.Equal(0xC0C0C0u, DxfImport.AciToRgb(9));
        Assert.Equal(0xFF3F00u, DxfImport.AciToRgb(20));
        Assert.Equal(0x7F0000u, DxfImport.AciToRgb(16));
        Assert.Equal(0x7F3F3Fu, DxfImport.AciToRgb(17));
        Assert.Equal(0x0000FFu, DxfImport.AciToRgb(170));
        Assert.Equal(0xFFFFFFu, DxfImport.AciToRgb(255));
    }

    // ---- PDF ----------------------------------------------------------------

    [Fact]
    public void Pdf_paths_are_in_mm_with_y_flipped()
    {
        var b = new PdfDocumentBuilder();
        var page = b.AddPage(200, 100); // points
        page.SetStrokeColor(255, 0, 0);
        page.DrawLine(new PdfPoint(10, 10), new PdfPoint(110, 10), 1);
        page.DrawRectangle(new PdfPoint(20, 50), 30, 40, 1, false);
        var r = PdfImport.Load(b.Build());

        const double k = 25.4 / 72;
        Assert.Equal(2, r.Paths.Count);
        Assert.Empty(r.Warnings);
        var line = r.Paths[0].Geometry.Subpaths[0];
        Near(new Vec2(10 * k, 90 * k), line.Start); // 10 pt above the bottom of a 100 pt page
        Near(new Vec2(110 * k, 90 * k), line.End);
        Assert.Equal(0xFF0000u, r.Paths[0].Color);

        var rect = r.Paths[1].Geometry.Bounds();
        Near(20 * k, rect.MinX); Near(10 * k, rect.MinY); // top edge at 90 pt → 10 pt from the top
        Near(30 * k, rect.Width); Near(40 * k, rect.Height);
        Assert.True(r.Paths[1].Geometry.Subpaths[0].Closed);
    }

    [Fact]
    public void Pdf_missing_page_and_non_pdf_are_rejected()
    {
        var b = new PdfDocumentBuilder();
        b.AddPage(100, 100).DrawLine(new PdfPoint(0, 0), new PdfPoint(10, 10), 1);
        var bytes = b.Build();
        Assert.Throws<FormatException>(() => PdfImport.Load(bytes, page: 2));
        Assert.Throws<FormatException>(() => PdfImport.Load(Encoding.ASCII.GetBytes("not a pdf")));
    }

    [Fact]
    public void Illustrator_without_pdf_compatibility_explains_why()
    {
        var path = Path.Combine(Path.GetTempPath(), $"photoncanvas-{Guid.NewGuid():N}.ai");
        File.WriteAllText(path, "%!PS-Adobe-3.0\n%%Creator: Adobe Illustrator(R) 8.0\n");
        try
        {
            var ex = Assert.Throws<FormatException>(() => Importer.Load(path));
            Assert.Contains("PDF compatibility", ex.Message);
        }
        finally { File.Delete(path); }
    }

    // ---- xTool projects -----------------------------------------------------

    const string PathDisplay = """
        {"id":"p1","type":"PATH","name":null,"x":40,"y":40,"angle":90,"scale":{"x":2,"y":2},"skew":{"x":0,"y":0},
         "pivot":{"x":0,"y":0},"offsetX":50,"offsetY":20,"width":20,"height":10,"zOrder":3,"visible":true,
         "layerColor":"#ff0000","layerTag":"#ff0000","dPath":"M0,0L10,0L10,5Z","isClosePath":true}
        """;

    const string RectDisplay = """
        {"id":"r1","type":"RECT","x":0,"y":0,"angle":0,"scale":{"x":1,"y":1},"offsetX":0,"offsetY":0,
         "width":10,"height":5,"radius":0,"zOrder":1,"visible":true,"layerColor":"#0000ff"}
        """;

    const string CircleDisplay = """
        {"id":"c1","type":"CIRCLE","x":100,"y":100,"angle":0,"scale":{"x":2,"y":1},"offsetX":100,"offsetY":100,
         "width":20,"height":10,"zOrder":2,"visible":true,"layerColor":"#ff0000"}
        """;

    const string HiddenDisplay = """
        {"id":"h1","type":"RECT","offsetX":0,"offsetY":0,"width":10,"height":5,"visible":false,"layerColor":"#ff0000"}
        """;

    static void CheckProjectShapes(ImportResult r)
    {
        // Sorted by zOrder: rect (1), circle (2), path (3).
        Assert.Equal(3, r.Paths.Count);
        Assert.Equal("Cut", r.LayerNames[0xFF0000]);
        Assert.Contains(r.Warnings, w => w.Contains("hidden"));

        var rect = r.Paths[0].Geometry.Bounds();
        Assert.Equal(0x0000FFu, r.Paths[0].Color);
        Near(0, rect.MinX); Near(0, rect.MinY); Near(10, rect.Width); Near(5, rect.Height);

        var circle = r.Paths[1].Geometry.Bounds();
        Near(100, circle.MinX, 0.01); Near(100, circle.MinY, 0.01);
        Near(20, circle.Width, 0.01); Near(10, circle.Height, 0.01);

        // Path: scale 2, rotate 90° clockwise on screen, then move by (50, 20):
        // (10,0) → (20,0) → (0,20) → (50,40); (10,5) → (20,10) → (−10,20) → (40,40).
        var p = r.Paths[2];
        Assert.Equal(0xFF0000u, p.Color);
        var sp = p.Geometry.Subpaths[0];
        Near(new Vec2(50, 20), sp.Start);
        Near(new Vec2(50, 40), sp.Segs[0].To);
        Near(new Vec2(40, 40), sp.Segs[1].To);
        Assert.True(sp.Closed);
    }

    [Fact]
    public void Xcs_v1_json_project()
    {
        var json = $$"""
            {"version":"1.3.6","canvasId":"c-1","extId":"GS004-CLASS-4","device":{"id":"x"},
             "canvas":[{"id":"c-1","title":"{panel}1",
                        "layerData":{"dataType":"Map","value":[["#ff0000",{"name":"Cut","order":1,"visible":true}]]},
                        "displays":[{{PathDisplay}},{{RectDisplay}},{{CircleDisplay}},{{HiddenDisplay}}]}]}
            """;
        CheckProjectShapes(XsImport.Parse(Encoding.UTF8.GetBytes(json)));
    }

    static byte[] TinyPng()
    {
        using var bmp = new SKBitmap(2, 1);
        bmp.SetPixel(0, 0, SKColors.Black);
        bmp.SetPixel(1, 0, SKColors.White);
        using var data = SKImage.FromBitmap(bmp).Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [Fact]
    public void Xs_v2_zip_project_with_picture_and_text()
    {
        var png = TinyPng();
        const string bitmap = """
            {"id":"b1","type":"BITMAP","x":10,"y":10,"angle":0,"scale":{"x":5,"y":5},"offsetX":10,"offsetY":10,
             "width":10,"height":5,"originWidth":2,"originHeight":1,"zOrder":4,"visible":true,"layerColor":"#000000",
             "resourcePath":"resources/abc.png","transform":[5,0,0,5,10,10]}
            """;
        const string text = """
            {"id":"t1","type":"TEXT","text":"I","x":0,"y":0,"angle":0,"scale":{"x":1,"y":1},"offsetX":200,"offsetY":200,
             "width":1,"height":10,"zOrder":5,"visible":true,"layerColor":"#ff0000",
             "charJSONs":[{"id":"g1","type":"PATH","angle":0,"scale":{"x":0.5,"y":0.5},"offsetX":300,"offsetY":310,
                           "dPath":"M0 -20L2 -20L2 0L0 0Z"}]}
            """;
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Put(string name, byte[] bytes)
            {
                using var s = zip.CreateEntry(name).Open();
                s.Write(bytes);
            }
            void PutText(string name, string s) => Put(name, Encoding.UTF8.GetBytes(s));
            PutText(".format", "v2");
            PutText("project.json", """{"__v2__":true,"version":"2.0.0","activeCanvasId":"cv","modules":{"canvases":["cv"],"devices":[]}}""");
            PutText("canvases/cv.json", """{"id":"cv","title":"{panel}1","layerData":{"#ff0000":{"name":"Cut","order":1,"visible":true}},"groupData":{}}""");
            PutText("canvases/cv/displays-0.json", $$"""{"canvasId":"cv","chunkIndex":0,"displays":[{{PathDisplay}},{{RectDisplay}}]}""");
            PutText("canvases/cv/displays-1.json", $$"""{"canvasId":"cv","chunkIndex":1,"displays":[{{CircleDisplay}},{{HiddenDisplay}},{{bitmap}},{{text}}]}""");
            Put("resources/abc.png", png);
        }
        var r = XsImport.Parse(ms.ToArray());

        var glyph = r.Paths.Single(p => p.Name == "Text \"I\"");
        r.Paths.Remove(glyph);
        CheckProjectShapes(r);
        // Glyph placement is absolute: (0,-20)·0.5 + (300,310) = (300,300).
        var gb = glyph.Geometry.Bounds();
        Near(300, gb.MinX); Near(300, gb.MinY); Near(1, gb.Width); Near(10, gb.Height);

        var img = Assert.Single(r.Images);
        Assert.Equal(png, img.Encoded);
        Near(new Vec2(10, 5), img.SizeMm);
        Near(new Vec2(10, 10), img.Transform.Apply(new Vec2(0, 0)));
        Near(new Vec2(20, 15), img.Transform.Apply(img.SizeMm));
    }

    [Fact]
    public void Xs_rejects_non_projects()
    {
        Assert.Throws<FormatException>(() => XsImport.Parse(Encoding.UTF8.GetBytes("""{"hello":1}""")));
        Assert.Throws<FormatException>(() => XsImport.Parse(Encoding.UTF8.GetBytes("not json")));
    }

    // ---- dispatcher and real samples -----------------------------------------

    [Fact]
    public void Importer_dispatches_by_extension_and_rejects_unknown()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"photoncanvas-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var dxf = Path.Combine(dir, "a.DXF");
            File.WriteAllText(dxf, Dxf(Line(0, 0, 10, 0)));
            Assert.Single(Importer.Load(dxf).Paths);

            var svg = Path.Combine(dir, "a.svg");
            File.WriteAllText(svg, """<svg xmlns="http://www.w3.org/2000/svg" width="10mm" height="10mm" viewBox="0 0 10 10"><rect width="10" height="5"/></svg>""");
            Near(5, Importer.Load(svg).Bounds.Height, 0.01);

            var ex = Assert.Throws<FormatException>(() => Importer.Load(Path.Combine(dir, "a.cdr")));
            Assert.Contains(".cdr", ex.Message);
        }
        finally { Directory.Delete(dir, true); }
    }

    /// <summary>
    /// The xTool tutorial projects from the locally extracted xTool Studio (reference/, not in git).
    /// Skipped when that folder is absent.
    /// </summary>
    [Fact]
    public void Reference_tutorial_projects_load()
    {
        var exts = Path.Combine(TestPaths.Root, "reference", "exts");
        if (!Directory.Exists(exts)) return;
        var files = Directory.GetDirectories(exts)
            .SelectMany(d => Directory.Exists(Path.Combine(d, "assets")) ? Directory.GetFiles(Path.Combine(d, "assets"), "Tutorial-*.xs") : [])
            .ToList();
        if (files.Count == 0) return;
        int withGeometry = 0;
        foreach (var f in files)
        {
            var r = Importer.Load(f);
            if (r.IsEmpty)
            {
                Assert.True(r.Warnings.Count > 0, $"{Path.GetFileName(f)} gave nothing and no explanation");
                continue;
            }
            withGeometry++;
            var b = r.Bounds;
            Assert.True(b.MinX > -50 && b.MinY > -50 && b.MaxX < 1000 && b.MaxY < 1000, $"{Path.GetFileName(f)} bounds {b}");
            Assert.All(r.Paths, p => Assert.False(p.Geometry.IsEmpty));
        }
        Assert.True(withGeometry >= files.Count / 2, $"only {withGeometry} of {files.Count} tutorials had geometry");
    }
}
