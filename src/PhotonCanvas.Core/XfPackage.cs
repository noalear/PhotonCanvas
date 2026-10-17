using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SkiaSharp;

namespace PhotonCanvas.Core;

/// <summary>
/// <c>.xf</c> job package: a POSIX ustar archive the F2 Ultra accepts as a job.
/// Members (in this order, mode 0666, uid/gid 0):
/// <c>preview.jpg</c>, <c>border.gcode</c>, <c>motion.gcode</c>, <c>description.json</c>.
/// </summary>
public static class XfPackage
{
    public sealed record Parts
    {
        public required string Motion { get; init; }
        public required string Border { get; init; }
        public required byte[] PreviewJpeg { get; init; }
        /// <summary><c>blue</c>, <c>red</c> or <c>mixed</c>.</summary>
        public required string Light { get; init; }
        /// <summary>One entry per processed item (informational; shown on the machine).</summary>
        public JsonArray Params { get; init; } = [];
        public DateTimeOffset Created { get; init; } = DateTimeOffset.Now;
        /// <summary>LASER_PLANE (bed), LASER_CYLINDER (rotary) or CONVEYOR_EXTENSION.</summary>
        public string Mode { get; init; } = "LASER_PLANE";
        /// <summary>The mechanical axis used ("U" with an attachment, else empty).</summary>
        public string Axis { get; init; } = "";
    }

    public static byte[] Build(Parts parts)
    {
        var desc = new JsonObject
        {
            ["mode"] = parts.Mode,
            ["light"] = parts.Light,
            ["create_time"] = parts.Created.ToString("yyyy/MM/dd HH:mm:ss"),
            ["axis"] = parts.Axis,
            ["processingParams"] = parts.Params.DeepClone(),
            ["material"] = 1,
        };
        var members = new (string Name, byte[] Data)[]
        {
            ("preview.jpg", parts.PreviewJpeg),
            ("border.gcode", Encoding.UTF8.GetBytes(parts.Border)),
            ("motion.gcode", Encoding.UTF8.GetBytes(parts.Motion)),
            ("description.json", Encoding.UTF8.GetBytes(desc.ToJsonString(new JsonSerializerOptions { WriteIndented = false }))),
        };
        long mtime = parts.Created.ToUnixTimeSeconds();
        using var ms = new MemoryStream();
        foreach (var (name, data) in members)
        {
            ms.Write(UstarHeader(name, data.Length, mtime));
            ms.Write(data);
            int pad = (512 - data.Length % 512) % 512;
            ms.Write(new byte[pad]);
        }
        ms.Write(new byte[1024]); // two zero blocks end the archive
        return ms.ToArray();
    }

    static byte[] UstarHeader(string name, long size, long mtime)
    {
        var h = new byte[512];
        void Put(int off, int len, string s)
        {
            var b = Encoding.ASCII.GetBytes(s);
            if (b.Length > len) throw new JobException($"Job package field too long: {s}");
            b.CopyTo(h, off);
        }
        string Octal(long v, int width) => Convert.ToString(v, 8).PadLeft(width - 1, '0');
        Put(0, 100, name);
        Put(100, 8, Octal(Convert.ToInt32("666", 8), 8));
        Put(108, 8, Octal(0, 8));
        Put(116, 8, Octal(0, 8));
        Put(124, 12, Octal(size, 12));
        Put(136, 12, Octal(mtime, 12));
        for (int i = 148; i < 156; i++) h[i] = (byte)' ';
        h[156] = (byte)'0';
        Put(257, 6, "ustar\0");
        Put(263, 2, "00");
        int sum = h.Sum(b => (int)b);
        Put(148, 8, Convert.ToString(sum, 8).PadLeft(6, '0') + "\0 ");
        return h;
    }

    /// <summary>Lists member names and sizes in a ustar archive (for checks and tests).</summary>
    public static List<(string Name, byte[] Data)> Read(byte[] tar)
    {
        var o = new List<(string, byte[])>();
        int pos = 0;
        while (pos + 512 <= tar.Length)
        {
            var hdr = tar.AsSpan(pos, 512);
            if (hdr.IndexOfAnyExcept((byte)0) < 0) break;
            var name = Encoding.ASCII.GetString(hdr[..100]).TrimEnd('\0');
            var sizeStr = Encoding.ASCII.GetString(hdr.Slice(124, 12)).Trim('\0', ' ');
            int size = Convert.ToInt32(sizeStr, 8);
            o.Add((name, tar.AsSpan(pos + 512, size).ToArray()));
            pos += 512 + (size + 511) / 512 * 512;
        }
        return o;
    }

    /// <summary>200×200 JPEG thumbnail of polylines, black on white.</summary>
    public static byte[] PreviewJpeg(IEnumerable<IReadOnlyList<Vec2>> paths)
    {
        const int Size = 200;
        var list = paths.ToList();
        using var surface = SKSurface.Create(new SKImageInfo(Size, Size, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);
        var b = Rect2.Bounds(list.SelectMany(p => p));
        if (!b.IsEmpty)
        {
            // Zoomed to fit and centred, like xTool Studio's previews (the picture shows the
            // shapes, not where they sit on the bed).
            double scale = (Size - 20) / Math.Max(1e-6, Math.Max(b.Width, b.Height));
            double ox = (Size - b.Width * scale) / 2, oy = (Size - b.Height * scale) / 2;
            using var paint = new SKPaint { Color = SKColors.Black, StrokeWidth = 2, IsAntialias = true, Style = SKPaintStyle.Stroke };
            foreach (var path in list)
            {
                if (path.Count < 2) continue;
                using var sk = new SKPath();
                for (int i = 0; i < path.Count; i++)
                {
                    float x = (float)((path[i].X - b.MinX) * scale + ox), y = (float)((path[i].Y - b.MinY) * scale + oy);
                    if (i == 0) sk.MoveTo(x, y); else sk.LineTo(x, y);
                }
                canvas.DrawPath(sk, paint);
            }
        }
        using var img = surface.Snapshot();
        using var data = img.Encode(SKEncodedImageFormat.Jpeg, 85);
        return data.ToArray();
    }
}
