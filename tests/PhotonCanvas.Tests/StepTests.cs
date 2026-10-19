using System.Globalization;
using System.Text;
using PhotonCanvas.Doc.Import;
using PhotonCanvas.Doc.Import.Step;
using Xunit;

namespace PhotonCanvas.Tests;

public class StepTests
{
    /// <summary>Builds small STEP files by hand: entities numbered as they're added.</summary>
    sealed class Step
    {
        readonly StringBuilder _data = new();
        int _next = 1;
        public string Units = "( LENGTH_UNIT() NAMED_UNIT(*) SI_UNIT(.MILLI.,.METRE.) )";

        public int Add(string entity)
        {
            int id = _next++;
            _data.Append(CultureInfo.InvariantCulture, $"#{id}={entity};\n");
            return id;
        }

        static string N(double v) => v.ToString("0.0###########", CultureInfo.InvariantCulture);
        public int Point(double x, double y, double z) => Add($"CARTESIAN_POINT('',({N(x)},{N(y)},{N(z)}))");
        public int Dir(double x, double y, double z) => Add($"DIRECTION('',({N(x)},{N(y)},{N(z)}))");
        public int Place(double x, double y, double z, (double, double, double)? axis = null, (double, double, double)? refDir = null)
        {
            var (ax, ay, az) = axis ?? (0, 0, 1);
            var (rx, ry, rz) = refDir ?? (1, 0, 0);
            return Add($"AXIS2_PLACEMENT_3D('',#{Point(x, y, z)},#{Dir(ax, ay, az)},#{Dir(rx, ry, rz)})");
        }
        public int Vertex(double x, double y, double z) => Add($"VERTEX_POINT('',#{Point(x, y, z)})");
        public int Line(int v1, int v2, (double X, double Y, double Z) a, (double X, double Y, double Z) b)
        {
            var d = (b.X - a.X, b.Y - a.Y, b.Z - a.Z);
            double l = Math.Sqrt(d.Item1 * d.Item1 + d.Item2 * d.Item2 + d.Item3 * d.Item3);
            int line = Add($"LINE('',#{Point(a.X, a.Y, a.Z)},#{Add($"VECTOR('',#{Dir(d.Item1 / l, d.Item2 / l, d.Item3 / l)},1.0)")})");
            return Add($"EDGE_CURVE('',#{v1},#{v2},#{line},.T.)");
        }
        public int Circle(int vertex, int place, double r) => Add($"EDGE_CURVE('',#{vertex},#{vertex},#{Add($"CIRCLE('',#{place},{N(r)})")},.T.)");
        public int Loop(params (int Edge, bool Forward)[] edges) =>
            Add($"EDGE_LOOP('',({string.Join(",", edges.Select(e => $"#{Add($"ORIENTED_EDGE('',*,*,#{e.Edge},{(e.Forward ? ".T." : ".F.")})")}"))}))");
        public int Face(int surface, bool same, params int[] loops) =>
            Add($"ADVANCED_FACE('',({string.Join(",", loops.Select((l, i) => $"#{Add($"{(i == 0 ? "FACE_OUTER_BOUND" : "FACE_BOUND")}('',#{l},.T.)")}"))}),#{surface},{(same ? ".T." : ".F.")})");
        public int Solid(params int[] faces) => Add($"MANIFOLD_SOLID_BREP('',#{Add($"CLOSED_SHELL('',({string.Join(",", faces.Select(f => $"#{f}"))}))")})");

        /// <summary>The finished file: a shape representation of the solids in a context with the units.</summary>
        public string Text(params int[] solids)
        {
            int unit = Add(Units);
            int ctx = Add($"( GEOMETRIC_REPRESENTATION_CONTEXT(3) GLOBAL_UNIT_ASSIGNED_CONTEXT((#{unit})) REPRESENTATION_CONTEXT('','') )");
            Add($"ADVANCED_BREP_SHAPE_REPRESENTATION('',({string.Join(",", solids.Select(s => $"#{s}"))}),#{ctx})");
            return $"ISO-10303-21;\nHEADER;\nFILE_SCHEMA(('AUTOMOTIVE_DESIGN'));\nENDSEC;\nDATA;\n{_data}ENDSEC;\nEND-ISO-10303-21;\n";
        }
    }

    /// <summary>A box from (x0,y0,z0) to (x1,y1,z1): six planes, twelve lines.</summary>
    static int Box(Step s, double x0, double y0, double z0, double x1, double y1, double z1)
    {
        var c = new (double X, double Y, double Z)[] { (x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0), (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1) };
        var v = c.Select(p => s.Vertex(p.X, p.Y, p.Z)).ToArray();
        int E(int a, int b) => s.Line(v[a], v[b], c[a], c[b]);
        int e01 = E(0, 1), e12 = E(1, 2), e23 = E(2, 3), e30 = E(3, 0), e45 = E(4, 5), e56 = E(5, 6), e67 = E(6, 7), e74 = E(7, 4), e04 = E(0, 4), e15 = E(1, 5), e26 = E(2, 6), e37 = E(3, 7);
        int Plane(double x, double y, double z, (double, double, double) n, (double, double, double) r) => s.Add($"PLANE('',#{s.Place(x, y, z, n, r)})");
        return s.Solid(
            s.Face(Plane(x0, y0, z0, (0, 0, -1), (1, 0, 0)), true, s.Loop((e30, false), (e23, false), (e12, false), (e01, false))),
            s.Face(Plane(x0, y0, z1, (0, 0, 1), (1, 0, 0)), true, s.Loop((e45, true), (e56, true), (e67, true), (e74, true))),
            s.Face(Plane(x0, y0, z0, (0, -1, 0), (1, 0, 0)), true, s.Loop((e01, true), (e15, true), (e45, false), (e04, false))),
            s.Face(Plane(x1, y0, z0, (1, 0, 0), (0, 1, 0)), true, s.Loop((e12, true), (e26, true), (e56, false), (e15, false))),
            s.Face(Plane(x0, y1, z0, (0, 1, 0), (-1, 0, 0)), true, s.Loop((e23, true), (e37, true), (e67, false), (e26, false))),
            s.Face(Plane(x0, y0, z0, (-1, 0, 0), (0, -1, 0)), true, s.Loop((e30, true), (e04, true), (e74, false), (e37, false))));
    }

    static double Volume(StepResult r) => r.Triangles.Sum(t => t.A.Dot(t.B.Cross(t.C))) / 6;

    /// <summary>Edges used once (welded by position): 0 for a sealed solid.</summary>
    static int OpenEdges(StepResult r)
    {
        var id = new Dictionary<(long, long, long), int>();
        int Id(P3 p) { var k = ((long)Math.Round(p.X * 1e5), (long)Math.Round(p.Y * 1e5), (long)Math.Round(p.Z * 1e5)); if (!id.TryGetValue(k, out var i)) id[k] = i = id.Count; return i; }
        var edges = new Dictionary<(int, int), int>();
        foreach (var (a, b, c) in r.Triangles)
        {
            int ia = Id(a), ib = Id(b), ic = Id(c);
            foreach (var (x, y) in new[] { (ia, ib), (ib, ic), (ic, ia) })
            {
                var k = x < y ? (x, y) : (y, x);
                edges[k] = edges.GetValueOrDefault(k) + 1;
            }
        }
        return edges.Count(e => e.Value != 2);
    }

    static StepResult Mesh(string text) => StepMesh.Mesh(StepFile.Parse(text));

    [Fact]
    public void ReadsTheFileFormat()
    {
        var f = StepFile.Parse("ISO-10303-21;\nHEADER;\nFILE_SCHEMA(('CONFIG_CONTROL_DESIGN'));\nENDSEC;\nDATA;\n" +
            "#1 = CARTESIAN_POINT ( 'it''s, a ; point', ( 1., -2.5E-1, 0.5 ) ) ;\n/* a comment */\n#2=( LENGTH_UNIT() NAMED_UNIT(*) SI_UNIT(.MILLI.,.METRE.) );\n" +
            "#3=MEASURE(LENGTH_MEASURE(25.4),#2,$);\nENDSEC;\nEND-ISO-10303-21;\n");
        Assert.Equal("CONFIG_CONTROL_DESIGN", f.Schema);
        var p = f.Get(1)!;
        Assert.Equal("CARTESIAN_POINT", p.Type);
        Assert.Equal("it's, a ; point", p.Args[0]);
        Assert.Equal([1.0, -0.25, 0.5], ((IReadOnlyList<object?>)p.Args[1]!).Cast<double>());
        Assert.True(f.Get(2)!.Is("SI_UNIT"));
        Assert.Equal(new StepEnum("MILLI"), f.Get(2)!.Of("SI_UNIT")![0]);
        var m = f.Get(3)!;
        Assert.Equal(25.4, ((StepTyped)m.Args[0]!).Args[0]);
        Assert.Equal(new StepRef(2), m.Args[1]);
        Assert.Null(m.Args[2]);
        Assert.Throws<FormatException>(() => StepFile.Parse("solid cube\nfacet normal 0 0 1\n"));
    }

    [Fact]
    public void BoxIsSealedAndTheRightSize()
    {
        var s = new Step();
        var r = Mesh(s.Text(Box(s, 0, 0, 0, 10, 20, 30)));
        Assert.Equal(6, r.Faces);
        Assert.Equal(0, r.FailedFaces);
        Assert.Equal(12, r.Triangles.Count);
        Assert.Equal(6000, Volume(r), 6);
        Assert.Equal(0, OpenEdges(r));
    }

    [Fact]
    public void CylinderFromCirclesAndACurvedWall()
    {
        const double R = 10, H = 25;
        var s = new Step();
        int bottom = s.Place(0, 0, 0), top = s.Place(0, 0, H);
        var e1 = s.Circle(s.Vertex(R, 0, 0), bottom, R);
        var e2 = s.Circle(s.Vertex(R, 0, H), top, R);
        int solid = s.Solid(
            s.Face(s.Add($"PLANE('',#{bottom})"), false, s.Loop((e1, false))),
            s.Face(s.Add($"PLANE('',#{top})"), true, s.Loop((e2, true))),
            s.Face(s.Add($"CYLINDRICAL_SURFACE('',#{bottom},{R})"), true, s.Loop((e1, true)), s.Loop((e2, false))));
        var r = Mesh(s.Text(solid));
        Assert.Equal(0, r.FailedFaces);
        Assert.Equal(0, OpenEdges(r));
        // The circles are divided within 0.05 mm: just under the true volume.
        double v = Volume(r), exact = Math.PI * R * R * H;
        Assert.InRange(v, exact * 0.99, exact);
        // Every point on the wall is on the cylinder.
        Assert.All(r.Triangles.SelectMany(t => new[] { t.A, t.B, t.C }), p => Assert.True(Math.Abs(Math.Sqrt(p.X * p.X + p.Y * p.Y) - R) < 1e-6 || Math.Abs(p.Z) < 1e-9 || Math.Abs(p.Z - H) < 1e-9));
    }

    [Fact]
    public void SphereClosesAtItsPoles()
    {
        const double R = 12;
        var s = new Step();
        int c = s.Place(0, 0, 0);
        var equator = s.Circle(s.Vertex(R, 0, 0), c, R);
        int sphere = s.Add($"SPHERICAL_SURFACE('',#{c},{R})");
        int solid = s.Solid(s.Face(sphere, true, s.Loop((equator, true))), s.Face(sphere, true, s.Loop((equator, false))));
        var r = Mesh(s.Text(solid));
        Assert.Equal(0, r.FailedFaces);
        Assert.Equal(0, OpenEdges(r));
        double v = Volume(r), exact = 4.0 / 3 * Math.PI * R * R * R;
        Assert.InRange(v, exact * 0.985, exact * 1.001);
        // Both caps, not one cap twice: points above and below the equator.
        Assert.Contains(r.Triangles, t => t.A.Z > R * 0.9);
        Assert.Contains(r.Triangles, t => t.A.Z < -R * 0.9);
    }

    [Fact]
    public void ConeFrustum()
    {
        // Radius 10 at the bottom, 4 at the top, 12 high: half-angle atan(6/12) inwards.
        const double R1 = 10, R2 = 4, H = 12;
        var s = new Step();
        int bottom = s.Place(0, 0, 0), top = s.Place(0, 0, H);
        var e1 = s.Circle(s.Vertex(R1, 0, 0), bottom, R1);
        var e2 = s.Circle(s.Vertex(R2, 0, H), top, R2);
        double half = Math.Atan((R1 - R2) / H);
        int solid = s.Solid(
            s.Face(s.Add($"PLANE('',#{bottom})"), false, s.Loop((e1, false))),
            s.Face(s.Add($"PLANE('',#{top})"), true, s.Loop((e2, true))),
            s.Face(s.Add($"CONICAL_SURFACE('',#{bottom},{R1},{-half})"), true, s.Loop((e1, true)), s.Loop((e2, false))));
        var r = Mesh(s.Text(solid));
        Assert.Equal(0, r.FailedFaces);
        Assert.Equal(0, OpenEdges(r));
        double exact = Math.PI * H / 3 * (R1 * R1 + R1 * R2 + R2 * R2);
        Assert.InRange(Volume(r), exact * 0.99, exact * 1.001);
    }

    [Fact]
    public void BoxWithARoundHoleThrough()
    {
        // A 40 × 40 × 10 block with a 10 mm radius hole down the middle.
        const double R = 10;
        var s = new Step();
        var c = new (double X, double Y, double Z)[] { (-20, -20, 0), (20, -20, 0), (20, 20, 0), (-20, 20, 0), (-20, -20, 10), (20, -20, 10), (20, 20, 10), (-20, 20, 10) };
        var v = c.Select(p => s.Vertex(p.X, p.Y, p.Z)).ToArray();
        int E(int a, int b) => s.Line(v[a], v[b], c[a], c[b]);
        int e01 = E(0, 1), e12 = E(1, 2), e23 = E(2, 3), e30 = E(3, 0), e45 = E(4, 5), e56 = E(5, 6), e67 = E(6, 7), e74 = E(7, 4), e04 = E(0, 4), e15 = E(1, 5), e26 = E(2, 6), e37 = E(3, 7);
        int bottom = s.Place(0, 0, 0), top = s.Place(0, 0, 10);
        var hb = s.Circle(s.Vertex(R, 0, 0), bottom, R);
        var ht = s.Circle(s.Vertex(R, 0, 10), top, R);
        int Plane(double x, double y, double z, (double, double, double) n, (double, double, double) r) => s.Add($"PLANE('',#{s.Place(x, y, z, n, r)})");
        int solid = s.Solid(
            s.Face(s.Add($"PLANE('',#{bottom})"), false, s.Loop((e30, false), (e23, false), (e12, false), (e01, false)), s.Loop((hb, true))),
            s.Face(s.Add($"PLANE('',#{top})"), true, s.Loop((e45, true), (e56, true), (e67, true), (e74, true)), s.Loop((ht, false))),
            s.Face(Plane(-20, -20, 0, (0, -1, 0), (1, 0, 0)), true, s.Loop((e01, true), (e15, true), (e45, false), (e04, false))),
            s.Face(Plane(20, -20, 0, (1, 0, 0), (0, 1, 0)), true, s.Loop((e12, true), (e26, true), (e56, false), (e15, false))),
            s.Face(Plane(-20, 20, 0, (0, 1, 0), (-1, 0, 0)), true, s.Loop((e23, true), (e37, true), (e67, false), (e26, false))),
            s.Face(Plane(-20, -20, 0, (-1, 0, 0), (0, -1, 0)), true, s.Loop((e30, true), (e04, true), (e74, false), (e37, false))),
            // The hole's wall faces in (towards the axis): against the cylinder's own normal.
            s.Face(s.Add($"CYLINDRICAL_SURFACE('',#{bottom},{R})"), false, s.Loop((hb, false)), s.Loop((ht, true))));
        var r = Mesh(s.Text(solid));
        Assert.Equal(0, r.FailedFaces);
        Assert.Equal(0, OpenEdges(r));
        double exact = 40 * 40 * 10 - Math.PI * R * R * 10;
        Assert.InRange(Volume(r), exact * 0.999, exact * 1.01);
    }

    [Fact]
    public void InchesBecomeMillimetres()
    {
        var s = new Step();
        int mm = s.Add("( LENGTH_UNIT() NAMED_UNIT(*) SI_UNIT(.MILLI.,.METRE.) )");
        int measure = s.Add($"LENGTH_MEASURE_WITH_UNIT(LENGTH_MEASURE(25.4),#{mm})");
        s.Units = $"( CONVERSION_BASED_UNIT('INCH',#{measure}) LENGTH_UNIT() NAMED_UNIT(#{s.Add("DIMENSIONAL_EXPONENTS(1.,0.,0.,0.,0.,0.,0.)")}) )";
        var r = Mesh(s.Text(Box(s, 0, 0, 0, 1, 2, 3)));
        Assert.Equal(6 * Math.Pow(25.4, 3), Volume(r), 3);
    }

    [Fact]
    public void AssemblyPartsGoWhereTheyArePlaced()
    {
        // One part (a 10 mm cube) used twice in an assembly: as it is, and moved 50 mm along x.
        var s = new Step();
        int unit = s.Add("( LENGTH_UNIT() NAMED_UNIT(*) SI_UNIT(.MILLI.,.METRE.) )");
        int ctx = s.Add($"( GEOMETRIC_REPRESENTATION_CONTEXT(3) GLOBAL_UNIT_ASSIGNED_CONTEXT((#{unit})) REPRESENTATION_CONTEXT('','') )");
        int cube = Box(s, 0, 0, 0, 10, 10, 10);
        int partOrigin = s.Place(0, 0, 0);
        int part = s.Add($"ADVANCED_BREP_SHAPE_REPRESENTATION('part',(#{cube},#{partOrigin}),#{ctx})");
        int at0 = s.Place(0, 0, 0), at50 = s.Place(50, 0, 0, (0, 0, 1), (0, 1, 0)); // also turned a quarter
        int assembly = s.Add($"SHAPE_REPRESENTATION('assembly',(#{at0},#{at50}),#{ctx})");
        foreach (var where in new[] { at0, at50 })
        {
            int idt = s.Add($"ITEM_DEFINED_TRANSFORMATION('','',#{partOrigin},#{where})");
            s.Add($"( REPRESENTATION_RELATIONSHIP('','',#{part},#{assembly}) REPRESENTATION_RELATIONSHIP_WITH_TRANSFORMATION(#{idt}) SHAPE_REPRESENTATION_RELATIONSHIP() )");
        }
        var text = $"ISO-10303-21;\nHEADER;\nENDSEC;\nDATA;\n{string.Join("", typeof(Step).GetField("_data", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(s))}ENDSEC;\nEND-ISO-10303-21;\n";
        var r = Mesh(text);
        Assert.Equal(2, r.Solids);
        Assert.Equal(2000, Volume(r), 6);
        var xs = r.Triangles.SelectMany(t => new[] { t.A, t.B, t.C }).ToList();
        // The copy at x = 50, turned so its x runs along y: from (40..50, 0..10).
        Assert.Equal(50, xs.Max(p => p.X), 6);
        Assert.Contains(xs, p => Math.Abs(p.X - 40) < 1e-9);
        Assert.Equal(0, xs.Min(p => p.X), 6);
    }

    [Fact]
    public void TriangulatesCadStyleOutlines()
    {
        // An outline with long runs of points in a straight line (edges split at other faces' corners),
        // holes lined up exactly with its corners and with each other, and a notch: the cases that trip
        // simple ear clipping. Every point must be used, nothing overlap, and the area come out exact.
        var pts = new List<Triangulator.V2>();
        int P(double x, double y) { pts.Add(new(x, y)); return pts.Count - 1; }
        var outer = new List<int>();
        for (int i = 0; i <= 12; i++) outer.Add(P(i * 5, 0));          // bottom: 13 points in a line
        outer.Add(P(60, 40));
        outer.Add(P(35, 40)); outer.Add(P(35, 30)); outer.Add(P(25, 30)); outer.Add(P(25, 40)); // a notch
        for (int i = 5; i >= 0; i--) outer.Add(P(i * 4, 40));         // top-left run
        outer.Add(P(0, 30)); outer.Add(P(0, 10));                       // left edge, split where the holes are
        var holes = new List<List<int>>
        {
            new() { P(5, 10), P(5, 30), P(15, 30), P(15, 10) },         // lined up with the left edge's points
            new() { P(20, 10), P(20, 20), P(40, 20), P(40, 10) },       // same height as the first
            new() { P(45, 5), P(45, 30), P(55, 30), P(55, 5) },
        };
        var loops = new List<IReadOnlyList<int>> { outer };
        loops.AddRange(holes);
        var tris = Triangulator.Fill(pts, loops);
        double Area((int A, int B, int C) t) => ((pts[t.B].X - pts[t.A].X) * (pts[t.C].Y - pts[t.A].Y) - (pts[t.C].X - pts[t.A].X) * (pts[t.B].Y - pts[t.A].Y)) / 2;
        double expected = 60 * 40 - 10 * 10 - 10 * 20 - 20 * 10 - 10 * 25;
        Assert.Equal(expected, tris.Sum(Area), 9);
        Assert.All(tris, t => Assert.True(Area(t) > 0));
        Assert.Equal(expected, Triangulator.EvenOddArea(pts, loops), 9);
        var used = tris.SelectMany(t => new[] { t.A, t.B, t.C }).ToHashSet();
        Assert.All(loops.SelectMany(l => l), i => Assert.Contains(i, used));
        var edges = tris.SelectMany(t => new[] { (t.A, t.B), (t.B, t.C), (t.C, t.A) }).Select(e => e.Item1 < e.Item2 ? e : (e.Item2, e.Item1)).GroupBy(e => e);
        Assert.All(edges, g => Assert.True(g.Count() <= 2));
    }

    [Fact]
    public void LoadsThroughTheModelReader()
    {
        var s = new Step();
        var path = Path.Combine(Path.GetTempPath(), $"pc-step-{Guid.NewGuid():N}.step");
        File.WriteAllText(path, s.Text(Box(s, 0, 0, 0, 5, 5, 5)));
        try
        {
            var tris = StlDepth.Load(path);
            Assert.Equal(12, tris.Count);
            Assert.Contains(".step", StlDepth.Extensions);
        }
        finally { File.Delete(path); }
    }
}
