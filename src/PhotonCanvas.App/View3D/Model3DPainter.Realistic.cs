namespace PhotonCanvas.App.View3D;

/// <summary>
/// The realistic look: each face is what it's made of (<see cref="Surface"/>: the sheet's wood grain, its charred
/// laser-cut edges with the plies showing, burnt engraving), lit by a warm sun that casts soft shadows (onto the
/// table the model stands on too) and a cool sky that lights what the sun doesn't, darker in corners and crevices
/// where little of the sky reaches (ambient occlusion). Light is worked out in linear light and eased back into
/// screen colours, like a photo's exposure.
/// <para>
/// The sun stays over the viewer's left shoulder however the model is turned, so the faces seen are lit and the
/// shadows fall away behind and to the right.
/// </para>
/// </summary>
public sealed partial class Model3DPainter
{
    /// <summary>The sun: how high (degrees above level) and how far round to the viewer's left (degrees).</summary>
    const double SunHeightDeg = 48, SunLeftDeg = 50;

    // The sun's light (warm) and the sky's from above (cool) and from the table below (bounced, warm).
    static readonly (float R, float G, float B) SunColour = (1.18f, 1.10f, 0.97f), SkyColour = (0.30f, 0.33f, 0.38f), BounceColour = (0.19f, 0.17f, 0.15f);

    /// <summary>How bright the picture is overall (as a camera's exposure).</summary>
    const float Exposure = 1.3f;

    // The shadow map: how deep (towards the sun) the nearest face is at each point of a picture taken from the sun, and which face it is.
    float[] _sDepth = [];
    object? _shadowFor;
    int[] _sId = [];
    int _sW, _sH;
    double _sKs, _sOx, _sOy;
    Vec3 _sR, _sU, _sD;

    // Per pixel: where in the model (mm), which face (−1 nothing, −2 the table), and how much of the sky reaches it.
    float[] _pPos = [], _ao = [], _aoTmp = [];
    int[] _pId = [];

    static readonly float[] Linear = Enumerable.Range(0, 256).Select(i => { double c = i / 255.0; return (float)(c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4)); }).ToArray();
    static readonly byte[] Gamma = Enumerable.Range(0, 4096).Select(i => { double c = i / 4095.0; return (byte)Math.Round(255 * (c <= 0.0031308 ? 12.92 * c : 1.055 * Math.Pow(c, 1 / 2.4) - 0.055)); }).ToArray();

    /// <summary>
    /// Each sample's colour: what its face is made of there (unlit, sRGB). Worked out once a pixel for each face in it
    /// (the samples of a pixel are for smoothing the edges between faces, not the grain); the finest grain fades out
    /// when a pixel covers more than it.
    /// </summary>
    void Materials(List<Scene3D.Face> faces, uint bg, int w, int h, int s, double pxPerMm)
    {
        double mmPerPx = 1 / Math.Max(1e-9, pxPerMm);
        Parallel.For(0, h, py =>
        {
            for (int px = 0; px < w; px++)
            {
                int lastId = int.MinValue;
                uint last = 0;
                for (int sy = 0; sy < s; sy++)
                {
                    int row = py * s + sy, i = row * _sw + px * s;
                    for (int sx = 0; sx < s; sx++, i++)
                    {
                        int id = _id[i];
                        if (id != lastId)
                        {
                            lastId = id;
                            if (id < 0) last = bg;
                            else if (faces[id].Look is not { } look) last = Pack(faces[id].Colour) | 0xFF000000u;
                            else
                            {
                                int c = px * s + sx;
                                var p = _r * ((c + 0.5 - _ox) / _ks) + _u * ((_oy - (row + 0.5)) / _ks) + _d * _depth[i];
                                last = Texture(look, p, mmPerPx);
                            }
                        }
                        _col[i] = last;
                    }
                }
            }
        });
    }

    /// <summary>What <paramref name="look"/> looks like at <paramref name="p"/> (unlit, sRGB).</summary>
    internal static uint Texture(in Surface look, Vec3 p, double mmPerPx = 0)
    {
        // How much of the fine detail (fibres, grit) shows: none once a pixel is wider than it.
        double fine = Math.Clamp(1.6 - mmPerPx * 4, 0, 1);
        var q = p - look.Origin;
        double a = q.Dot(look.Along), b = q.Dot(look.Across);
        // Each piece a little different, and its grain not lined up with the next piece's.
        uint seed = Seed(look.Origin);
        double sa = (seed & 0xFFF) * 0.37, sb = (seed >> 12 & 0xFFF) * 0.53, tone = 1 + ((seed >> 24) / 255.0 - 0.5) * 0.08;
        double r = Linear[look.Albedo.R], g = Linear[look.Albedo.G], bl = Linear[look.Albedo.B];
        double k, warm = 0;
        switch (look.Kind)
        {
            case SurfaceKind.Grain:
            {
                a += sa; b += sb;
                // Wavy grain lines along the piece, some stronger than others and unevenly spaced, fine fibres between
                // them, and broad blotches of lighter and darker wood.
                double wave = (Fbm(a * 0.012, b * 0.05) - 0.5) * 10;
                double gr = Noise(a * 0.018, (b + wave) * 0.85);
                double line = Math.Clamp((gr - 0.58) / 0.3, 0, 1);
                line *= line * (3 - 2 * line);
                double fibre = Noise(a * 0.35, b * 7) - 0.5;
                double blotch = Fbm(a * 0.02, b * 0.035) - 0.5;
                k = tone * (1 - 0.13 * line + 0.05 * fine * fibre + 0.14 * blotch);
                warm = 0.5 * line;
                break;
            }
            case SurfaceKind.Cut:
            {
                // Charred brown by the laser, the plies (and the glue between them) showing through the sheet's thickness,
                // with the beam's fine marks running across it.
                const double cr = 0.045, cg = 0.022, cb = 0.011;
                r = r * 0.2 + cr * 0.8; g = g * 0.2 + cg * 0.8; bl = bl * 0.2 + cb * 0.8;
                double t = Math.Max(0.5, look.Thickness);
                int plies = Math.Clamp((int)Math.Round(t / 1.0), 1, 9);
                double u = Math.Clamp(b / t, 0, 1) * plies, f = u - Math.Floor(u);
                int ply = Math.Min(plies - 1, (int)u);
                double glue = plies > 1 && u > 0.2 && u < plies - 0.2 ? Math.Max(0, 1 - Math.Min(f, 1 - f) / 0.07) : 0;
                double streak = Noise((a + sa) * 3.2, b * 0.4) - 0.5;
                // The plies and glue fade out when a pixel is as thick as a ply (they'd only flicker).
                double plyShows = Math.Clamp(1.5 - mmPerPx * 2.5, 0, 1);
                k = tone * (1 + plyShows * ((ply % 2 == 0 ? 0.08 : -0.05) - 0.45 * glue) + 0.22 * fine * streak);
                break;
            }
            case SurfaceKind.Burnt:
            {
                a += sa; b += sb;
                // Rough and mottled: burnt more in some places than others.
                double mottle = Fbm(a * 0.6, b * 0.6) - 0.5, grit = Noise(a * 5, b * 5) - 0.5;
                k = 1 + 0.55 * mottle + 0.25 * fine * grit;
                break;
            }
            default:
                k = 1;
                break;
        }
        // Darker grain is a little redder.
        double kr = k * (1 + 0.04 * warm), kg = k * (1 - 0.05 * warm), kb = k * (1 - 0.16 * warm);
        return 0xFF000000u | (uint)ToByte(r * kr) << 16 | (uint)ToByte(g * kg) << 8 | ToByte(bl * kb);
    }

    static byte ToByte(double linear) => Gamma[(int)(Math.Clamp(linear, 0, 1) * 4095 + 0.5)];

    static uint Seed(Vec3 o)
    {
        unchecked
        {
            uint h = (uint)(int)Math.Round(o.X * 10) * 0x9E3779B1u ^ (uint)(int)Math.Round(o.Y * 10) * 0x85EBCA77u ^ (uint)(int)Math.Round(o.Z * 10) * 0xC2B2AE3Du;
            h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15;
            return h;
        }
    }

    static double Hash(int x, int y)
    {
        unchecked
        {
            uint h = (uint)x * 374761393u + (uint)y * 668265263u;
            h = (h ^ h >> 13) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777215.0;
        }
    }

    /// <summary>Smooth value noise, 0–1.</summary>
    static double Noise(double x, double y)
    {
        double fx = Math.Floor(x), fy = Math.Floor(y);
        int xi = (int)fx, yi = (int)fy;
        double tx = x - fx, ty = y - fy;
        tx = tx * tx * (3 - 2 * tx);
        ty = ty * ty * (3 - 2 * ty);
        double a = Hash(xi, yi), b = Hash(xi + 1, yi), c = Hash(xi, yi + 1), d = Hash(xi + 1, yi + 1);
        return a + (b - a) * tx + (c - a) * ty + (a - b - c + d) * tx * ty;
    }

    /// <summary>Three octaves of <see cref="Noise"/>, 0–1.</summary>
    static double Fbm(double x, double y) => (Noise(x, y) * 4 + Noise(x * 2.03 + 17.1, y * 2.03 + 3.7) * 2 + Noise(x * 4.1 + 5.3, y * 4.1 + 31.9)) / 7;

    /// <summary>Where the realistic look's sun is, seen by <paramref name="cam"/> (a unit vector towards it): high over the viewer's left shoulder.</summary>
    public static Vec3 SunFor(Camera3D cam)
    {
        var up = cam.SkyOrY;
        Vec3 Flat(Vec3 v) => v - up * v.Dot(up);
        // Towards the viewer, level: from the way the camera looks and the way the screen's up leans (one of them is always level enough).
        var toward = Flat(cam.Toward) - Flat(cam.Up);
        if (toward.Length < 1e-6) toward = Flat(cam.Right).Cross(up);
        toward = toward.Unit;
        var right = up.Cross(toward).Unit * -1;
        if (right.Dot(cam.Right) < 0) right = -right;
        double h = SunHeightDeg * Math.PI / 180, l = SunLeftDeg * Math.PI / 180;
        return (up * Math.Sin(h) + (toward * Math.Cos(l) - right * Math.Sin(l)) * Math.Cos(h)).Unit;
    }

    /// <summary>Lights every sample: sun and shadow, sky and the corners it can't reach, a little shine.</summary>
    void Light(Scene3D scene, List<Scene3D.Face> faces, ulong hidden, Camera3D cam, int w, int h, int s, double pxPerMm, uint bg, bool draft)
    {
        var up = cam.SkyOrY;
        var sun = SunFor(cam);
        bool Hidden(int group) => group is >= 0 and < 64 && (hidden >> group & 1) != 0;

        // The table: level with the bottom of the model, seen (and shadowed) only from above.
        double floor = double.MaxValue, size = 0;
        double x0 = double.MaxValue, x1 = double.MinValue, y0 = double.MaxValue, y1 = double.MinValue, z0 = double.MaxValue, z1 = double.MinValue;
        foreach (var f in faces)
        {
            if (f.Ghost || Hidden(f.Group)) continue;
            foreach (var l in f.Loops)
                foreach (var p in l)
                {
                    floor = Math.Min(floor, p.Dot(up));
                    x0 = Math.Min(x0, p.X); x1 = Math.Max(x1, p.X); y0 = Math.Min(y0, p.Y); y1 = Math.Max(y1, p.Y); z0 = Math.Min(z0, p.Z); z1 = Math.Max(z1, p.Z);
                }
        }
        if (floor == double.MaxValue) return;
        size = new Vec3(x1 - x0, y1 - y0, z1 - z0).Length;
        double dUp = _d.Dot(up);
        bool table = dUp > 0.03;

        ShadowMap(scene, faces, hidden, sun, draft);

        // Per pixel (its middle sample): where it is in the model, and which face.
        int np = w * h;
        Ensure(ref _pPos, np * 3);
        Ensure(ref _pId, np);
        Ensure(ref _ao, np);
        Ensure(ref _aoTmp, np);
        int half = s / 2;
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                int i = (y * s + half) * _sw + x * s + half, pi = y * w + x;
                double sx = (x * s + half + 0.5 - _ox) / _ks, sy = (_oy - (y * s + half + 0.5)) / _ks;
                int id = _id[i];
                Vec3 p;
                if (id >= 0) p = _r * sx + _u * sy + _d * _depth[i];
                else if (table)
                {
                    var p0 = _r * sx + _u * sy;
                    p = p0 + _d * ((floor - p0.Dot(up)) / dUp);
                    id = -2;
                }
                else { _pId[pi] = -1; continue; }
                _pId[pi] = id;
                _pPos[pi * 3] = (float)p.X; _pPos[pi * 3 + 1] = (float)p.Y; _pPos[pi * 3 + 2] = (float)p.Z;
            }
        });

        Occlusion(faces, up, w, h, Math.Clamp(size * 0.022, 1.2, 7), pxPerMm, draft);

        // Every sample lit: the light worked out once a pixel for each face in it (or the table), then each sample's own
        // colour (its grain, the engraving and scored lines on it) lit by it.
        var sunC = SunColour; var skyC = SkyColour; var bounceC = BounceColour;
        var view = _d;
        uint bgOpaque = bg | 0xFF000000u;
        float bgR = Linear[bg >> 16 & 0xFF], bgG = Linear[bg >> 8 & 0xFF], bgB = Linear[bg & 0xFF];
        double texel = 1 / _sKs;
        Parallel.For(0, h, py =>
        {
            for (int px = 0; px < w; px++)
            {
                int pi = py * w + px;
                float ao = _pId[pi] == -1 ? 1 : _ao[pi];
                // A different turn of the shadow's taps at each pixel, so its soft edge is smooth rather than stepped.
                double turn = draft ? 0 : ((px & 3) * 4 + (py & 3) + ((px >> 2 ^ py >> 2) & 3) * 0.37) * 0.3927;
                int lastId = int.MinValue;
                double lr = 0, lg = 0, lb = 0, sr = 0, sg = 0, sb = 0;
                uint table0 = bgOpaque;
                for (int sy = 0; sy < s; sy++)
                {
                    int row = py * s + sy, i = row * _sw + px * s;
                    for (int sx = 0; sx < s; sx++, i++)
                    {
                        int id = _id[i];
                        if (id != lastId)
                        {
                            lastId = id;
                            double cx = (px * s + sx + 0.5 - _ox) / _ks, cy = (_oy - (row + 0.5)) / _ks;
                            if (id < 0)
                            {
                                if (!table) { table0 = bgOpaque; continue; }
                                var p0 = _r * cx + _u * cy;
                                var p = p0 + _d * ((floor - p0.Dot(up)) / dUp);
                                double nl = up.Dot(sun);
                                double lit = nl > 0 ? Sunlit(p, -2, nl, texel, turn) : 0;
                                // The table keeps the view's colour, darkened by the model's shadow and in close round its foot.
                                double k = (1 - 0.42 * (1 - lit)) * (0.45 + 0.55 * ao);
                                table0 = 0xFF000000u | (uint)ToByte(bgR * k) << 16 | (uint)ToByte(bgG * k) << 8 | ToByte(bgB * k);
                            }
                            else
                            {
                                var f = faces[id];
                                var n = f.Normal;
                                var pos = _r * cx + _u * cy + _d * _depth[i];
                                double ndl = n.Dot(sun);
                                double sh = ndl > 0 ? Sunlit(pos, id, ndl, texel, turn) : 0;
                                double direct = Math.Max(0, ndl) * sh;
                                // The sky from above, the table's bounce from below.
                                double t = 0.5 + 0.5 * n.Dot(up);
                                lr = sunC.R * direct + (bounceC.R + (skyC.R - bounceC.R) * t) * ao;
                                lg = sunC.G * direct + (bounceC.G + (skyC.G - bounceC.G) * t) * ao;
                                lb = sunC.B * direct + (bounceC.B + (skyC.B - bounceC.B) * t) * ao;
                                // A little shine where the sun glances off towards the viewer (more on engraving's burnt
                                // resin, hardly any on charred edges).
                                double shine = (f.Look?.Kind ?? SurfaceKind.Plain) switch { SurfaceKind.Grain => 0.05, SurfaceKind.Burnt => 0.07, SurfaceKind.Cut => 0.015, _ => 0.10 };
                                double spec = direct > 0 ? shine * Math.Pow(Math.Max(0, n.Dot((sun + view).Unit)), 28) * sh : 0;
                                sr = spec * sunC.R; sg = spec * sunC.G; sb = spec * sunC.B;
                            }
                        }
                        if (id < 0) { _col[i] = table0; continue; }
                        uint col = _col[i];
                        _col[i] = 0xFF000000u | (uint)Toned(Linear[col >> 16 & 0xFF] * lr + sr) << 16 | (uint)Toned(Linear[col >> 8 & 0xFF] * lg + sg) << 8 | Toned(Linear[col & 0xFF] * lb + sb);
                    }
                }
            }
        });
    }

    /// <summary>Light eased into the screen's range (bright light rolls off instead of burning out), as a screen colour 0–255.</summary>
    static byte Toned(double x) => x <= 0 ? (byte)0 : x >= ToneTop ? ToneLut[^1] : ToneLut[(int)(x * ToneSteps)];

    const double ToneTop = 6, ToneSteps = 1024;

    static readonly byte[] ToneLut = Enumerable.Range(0, (int)(ToneTop * ToneSteps) + 1).Select(i => ToByte(1 - Math.Exp(-(i + 0.5) / ToneSteps * Exposure))).ToArray();

    /// <summary>
    /// The picture the sun would take of the model (straight along its rays): how near the sun each face is, for
    /// <see cref="Sunlit"/>. Hidden groups and ghosts cast no shadow.
    /// </summary>
    void ShadowMap(Scene3D scene, List<Scene3D.Face> faces, ulong hidden, Vec3 sun, bool draft)
    {
        // The same as last time (the model zoomed or moved, but not turned or changed): keep it.
        var key = (scene, scene.Version, hidden, sun, draft);
        if (key.Equals(_shadowFor)) return;
        _shadowFor = key;
        bool Hidden(int group) => group is >= 0 and < 64 && (hidden >> group & 1) != 0;
        var d = sun;
        var r = (Math.Abs(d.Y) < 0.9 ? new Vec3(0, 1, 0) : new Vec3(1, 0, 0)).Cross(d).Unit;
        var u = d.Cross(r);
        double x0 = double.MaxValue, x1 = double.MinValue, y0 = double.MaxValue, y1 = double.MinValue;
        foreach (var f in faces)
        {
            if (f.Ghost || Hidden(f.Group)) continue;
            foreach (var l in f.Loops)
                foreach (var p in l)
                {
                    double x = p.Dot(r), y = p.Dot(u);
                    x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); y0 = Math.Min(y0, y); y1 = Math.Max(y1, y);
                }
        }
        int res = draft ? 1024 : 1600;
        double ks = Math.Min(40, Math.Min((res - 4) / Math.Max(1e-3, x1 - x0), (res - 4) / Math.Max(1e-3, y1 - y0)));
        int sw = Math.Max(1, (int)Math.Ceiling((x1 - x0) * ks) + 4), sh = Math.Max(1, (int)Math.Ceiling((y1 - y0) * ks) + 4);
        _sR = r; _sU = u; _sD = d; _sKs = ks; _sW = sw; _sH = sh;
        _sOx = 2 - x0 * ks;
        _sOy = 2 + y1 * ks;
        Ensure(ref _sDepth, sw * sh);
        Ensure(ref _sId, sw * sh);
        Array.Fill(_sDepth, float.NegativeInfinity, 0, sw * sh);
        Array.Fill(_sId, -1, 0, sw * sh);

        // The faces drawn into it as into the screen, from the sun.
        var keep = (_r, _u, _d, _ox, _oy, _ks, _sw, _sh, _depth, _id);
        (_r, _u, _d, _ox, _oy, _ks, _sw, _sh, _depth, _id) = (r, u, d, _sOx, _sOy, ks, sw, sh, _sDepth, _sId);
        try
        {
            for (int i = 0; i < faces.Count; i++)
                if (!faces[i].Ghost && !Hidden(faces[i].Group) && faces[i].Normal.Dot(d) > 1e-6) Face(faces[i], i, ghost: false);
        }
        finally
        {
            (_r, _u, _d, _ox, _oy, _ks, _sw, _sh, _depth, _id) = keep;
        }
    }

    // Taps round a point of the shadow map, to soften the shadow's edge (a circle of them).
    static readonly (double X, double Y)[] Taps =
    [
        (0.0, 0.0), (0.53, 0.18), (-0.21, 0.52), (-0.47, -0.30), (0.17, -0.55),
        (0.98, 0.10), (0.62, 0.78), (-0.25, 0.96), (-0.86, 0.48), (-0.93, -0.37), (-0.35, -0.94), (0.52, -0.85),
    ];

    /// <summary>How much of the sun reaches <paramref name="p"/> on face <paramref name="face"/> (0–1), from the shadow map.</summary>
    double Sunlit(Vec3 p, int face, double nl, double texel, double turn)
    {
        double lx = _sOx + p.Dot(_sR) * _sKs, ly = _sOy - p.Dot(_sU) * _sKs, z = p.Dot(_sD);
        // The softness: a few tenths of a mm, at least a couple of the map's dots.
        double radius = Math.Max(1.6, 0.35 * _sKs);
        double tan = Math.Min(6, Math.Sqrt(Math.Max(0, 1 - nl * nl)) / Math.Max(nl, 1e-3));
        double bias = texel * (1.5 + (radius + 1) * tan) + 0.04;
        double cs = Math.Cos(turn), sn = Math.Sin(turn);
        int lit = 0;
        foreach (var (tx, ty) in Taps)
        {
            int ix = (int)Math.Floor(lx + (tx * cs - ty * sn) * radius), iy = (int)Math.Floor(ly + (tx * sn + ty * cs) * radius);
            if (ix < 0 || iy < 0 || ix >= _sW || iy >= _sH) { lit++; continue; }
            int t = iy * _sW + ix;
            int id = _sId[t];
            if (id < 0 || id == face || _sDepth[t] <= z + bias) lit++;
        }
        return lit / (double)Taps.Length;
    }

    /// <summary>
    /// How much of the sky reaches each pixel (1 all of it): darker where the model rises round it close by, within
    /// <paramref name="radiusMm"/> (screen-space ambient occlusion), then smoothed over each face.
    /// </summary>
    void Occlusion(List<Scene3D.Face> faces, Vec3 up, int w, int h, double radiusMm, double pxPerMm, bool draft)
    {
        int np = w * h;
        double rpx = radiusMm * pxPerMm;
        if (rpx < 1.5)
        {
            Array.Fill(_ao, 1f, 0, np);
            return;
        }
        rpx = Math.Min(rpx, 90);
        int taps = draft ? 8 : 14, turns = draft ? 1 : 16;
        double r2 = radiusMm * radiusMm;
        // Where each tap goes (pixels), for each of 16 turns of the spiral (a different one at each pixel of a 4 × 4 block).
        var offX = new int[turns * taps];
        var offY = new int[turns * taps];
        for (int t = 0; t < turns; t++)
            for (int j = 0; j < taps; j++)
            {
                double ang = j * 2.399963229728653 + t * (2 * Math.PI / 16), rad = rpx * Math.Sqrt((j + 0.5) / taps);
                offX[t * taps + j] = (int)Math.Round(Math.Cos(ang) * rad);
                offY[t * taps + j] = (int)Math.Round(Math.Sin(ang) * rad);
            }
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                int pi = y * w + x, id = _pId[pi];
                if (id == -1) { _ao[pi] = 1; continue; }
                var n = id >= 0 ? faces[id].Normal : up;
                double px = _pPos[pi * 3], py = _pPos[pi * 3 + 1], pz = _pPos[pi * 3 + 2];
                int at = (turns == 1 ? 0 : (x & 3) * 4 + (y & 3)) * taps;
                double occ = 0;
                for (int j = 0; j < taps; j++)
                {
                    int qx = x + offX[at + j], qy = y + offY[at + j];
                    if (qx < 0 || qy < 0 || qx >= w || qy >= h) continue;
                    int qi = qy * w + qx;
                    if (_pId[qi] == -1) continue;
                    double vx = _pPos[qi * 3] - px, vy = _pPos[qi * 3 + 1] - py, vz = _pPos[qi * 3 + 2] - pz;
                    double dd = vx * vx + vy * vy + vz * vz;
                    if (dd < 1e-6) continue;
                    double fall = 1 - dd / (4 * r2);
                    if (fall <= 0) continue;
                    double cosv = (vx * n.X + vy * n.Y + vz * n.Z) / Math.Sqrt(dd);
                    if (cosv <= 0.12) continue;
                    occ += (cosv - 0.12) * fall;
                }
                _ao[pi] = (float)Math.Clamp(1 - 2.2 * occ / taps, 0, 1);
            }
        });
        if (draft) return;
        // Smoothed over 4 × 4 pixels of the same face (or the table), to hide the taps' pattern without blurring across edges.
        var src = _ao; var dst = _aoTmp;
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                int pi = y * w + x, id = _pId[pi];
                if (id == -1) { dst[pi] = 1; continue; }
                float sum = 0; int cnt = 0;
                for (int yy = Math.Max(0, y - 2); yy <= Math.Min(h - 1, y + 1); yy++)
                    for (int xx = Math.Max(0, x - 2); xx <= Math.Min(w - 1, x + 1); xx++)
                    {
                        int qi = yy * w + xx;
                        if (_pId[qi] != id) continue;
                        sum += src[qi]; cnt++;
                    }
                dst[pi] = cnt > 0 ? sum / cnt : src[pi];
            }
        });
        (_ao, _aoTmp) = (_aoTmp, _ao);
    }
}
