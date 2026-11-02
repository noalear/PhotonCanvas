using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;

namespace PhotonCanvas.Addon.Topo;

/// <summary>
/// Elevation GeoTIFFs (what USGS, Copernicus, OpenTopography and QGIS save): one band of heights
/// as whole or floating-point numbers, in strips or tiles, uncompressed, LZW, Deflate or PackBits,
/// with or without a predictor. Read straight into a <see cref="GridReducer"/>, so big surveys are
/// averaged down as they're read. The spacing comes from the GeoTIFF tags: degrees (turned into
/// metres at the file's latitude) or a projection's metres or feet.
/// </summary>
static class GeoTiff
{
    public static bool IsTiff(ReadOnlySpan<byte> d) => d.Length >= 8 && ((d[0] == 'I' && d[1] == 'I' && d[2] == 42 && d[3] == 0) || (d[0] == 'M' && d[1] == 'M' && d[2] == 0 && d[3] == 42));

    sealed class Ifd(Dictionary<int, (int Type, long Count, long ValueAt)> tags)
    {
        public readonly Dictionary<int, (int Type, long Count, long ValueAt)> Tags = tags;
    }

    public static DemGrid Read(byte[] d, string name, int maxSide)
    {
        if (d.Length >= 4 && ((d[0] == 'I' && d[2] == 43) || (d[0] == 'M' && d[3] == 43)))
            throw new FormatException("It's a BigTIFF: save it as an ordinary GeoTIFF (under 4 GB), or cut out the part you want first.");
        if (!IsTiff(d)) throw new FormatException("It isn't a TIFF file.");
        bool le = d[0] == 'I';
        ushort U16(long at) => le ? BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan((int)at)) : BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan((int)at));
        uint U32(long at) => le ? BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan((int)at)) : BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan((int)at));
        double F64(long at) => BitConverter.Int64BitsToDouble(le ? BinaryPrimitives.ReadInt64LittleEndian(d.AsSpan((int)at)) : BinaryPrimitives.ReadInt64BigEndian(d.AsSpan((int)at)));

        long ifdAt = U32(4);
        if (ifdAt <= 0 || ifdAt + 2 > d.Length) throw new FormatException("The TIFF file is damaged (no picture directory).");
        int n = U16(ifdAt);
        var tags = new Dictionary<int, (int, long, long)>();
        for (int i = 0; i < n; i++)
        {
            long e = ifdAt + 2 + 12L * i;
            if (e + 12 > d.Length) throw new FormatException("The TIFF file is cut short.");
            int tag = U16(e), type = U16(e + 2);
            long count = U32(e + 4);
            int size = type switch { 1 or 2 or 6 or 7 => 1, 3 or 8 => 2, 4 or 9 or 11 => 4, 5 or 10 or 12 => 8, 16 or 17 or 18 => 8, _ => 1 };
            long at = size * count <= 4 ? e + 8 : U32(e + 8);
            tags[tag] = (type, count, at);
        }
        var ifd = new Ifd(tags);

        long[] Values(int tag)
        {
            if (!ifd.Tags.TryGetValue(tag, out var t)) return [];
            var r = new long[t.Count];
            for (long i = 0; i < t.Count; i++)
                r[i] = t.Type switch
                {
                    3 => U16(t.ValueAt + 2 * i),
                    4 => U32(t.ValueAt + 4 * i),
                    1 => d[t.ValueAt + i],
                    _ => throw new FormatException($"The TIFF file has an unexpected kind of number in tag {tag}."),
                };
            return r;
        }
        long One(int tag, long fallback) => Values(tag) is { Length: > 0 } v ? v[0] : fallback;
        double[] Doubles(int tag)
        {
            if (!ifd.Tags.TryGetValue(tag, out var t) || t.Type != 12) return [];
            return [.. Enumerable.Range(0, (int)t.Count).Select(i => F64(t.ValueAt + 8L * i))];
        }
        string Text(int tag) => ifd.Tags.TryGetValue(tag, out var t) && t.Type == 2 ? System.Text.Encoding.ASCII.GetString(d, (int)t.ValueAt, (int)t.Count).TrimEnd('\0', ' ') : "";

        int w = (int)One(256, 0), h = (int)One(257, 0);
        int bits = (int)One(258, 1), spp = (int)One(277, 1), format = (int)One(339, 1);
        int compression = (int)One(259, 1), predictor = (int)One(317, 1), planar = (int)One(284, 1);
        if (w <= 0 || h <= 0) throw new FormatException("The TIFF file has no picture in it.");
        if (bits is not (8 or 16 or 32 or 64)) throw new FormatException($"The TIFF has {bits}-bit samples: heights need 8, 16, 32 or 64.");
        if (format is not (1 or 2 or 3)) throw new FormatException("The TIFF's numbers aren't whole or floating-point ones.");
        if (compression is not (1 or 5 or 8 or 32946 or 32773))
            throw new FormatException(compression switch
            {
                7 => "The TIFF is JPEG-compressed, which spoils heights: save it uncompressed, LZW or Deflate (in QGIS: Export → Save As).",
                34887 => "The TIFF is LERC-compressed: save it uncompressed, LZW or Deflate (in QGIS: Export → Save As).",
                50000 => "The TIFF is ZSTD-compressed: save it uncompressed, LZW or Deflate (in QGIS: Export → Save As).",
                _ => $"The TIFF's compression ({compression}) isn't one this reads: save it uncompressed, LZW or Deflate.",
            });
        int bytesPer = bits / 8;
        // Samples of a pixel side by side (chunky) or each band on its own (planar): band 1 only either way.
        int pixelStride = planar == 2 ? 1 : spp;

        bool tiled = ifd.Tags.ContainsKey(322);
        int tw = tiled ? (int)One(322, 256) : w, th = tiled ? (int)One(323, 256) : (int)Math.Min(One(278, h), h);
        var offsets = Values(tiled ? 324 : 273);
        var counts = Values(tiled ? 325 : 279);
        if (offsets.Length == 0) throw new FormatException("The TIFF file has no picture data.");
        int across = (w + tw - 1) / tw, down = (h + th - 1) / th;

        double nodata = double.TryParse(Text(42113), NumberStyles.Float, CultureInfo.InvariantCulture, out var nd) ? nd : double.NaN;
        var reducer = new GridReducer(w, h, maxSide);
        int rowBytes = tw * pixelStride * bytesPer;
        for (int b = 0; b < across * down && b < offsets.Length; b++)
        {
            long off = offsets[b], len = b < counts.Length ? counts[b] : d.Length - off;
            if (off <= 0 || off + len > d.Length) throw new FormatException("The TIFF file is cut short.");
            int bx = tiled ? b % across * tw : 0, by = tiled ? b / across * th : b * th;
            int rows = tiled ? th : Math.Min(th, h - by);
            var block = Decompress(d.AsSpan((int)off, (int)len), compression, rowBytes * rows);
            if (predictor == 2) UndoDifferences(block, rowBytes, rows, pixelStride, bytesPer, le);
            else if (predictor == 3) UndoFloatPredictor(block, tw * pixelStride, rows, bytesPer, le);
            bool sampleLe = le && predictor != 3 || predictor == 3 && BitConverter.IsLittleEndian;
            for (int y = 0; y < rows && by + y < h; y++)
                for (int x = 0; x < tw && bx + x < w; x++)
                {
                    int at = y * rowBytes + x * pixelStride * bytesPer;
                    if (at + bytesPer > block.Length) break;
                    double v = Sample(block.AsSpan(at, bytesPer), format, bits, sampleLe);
                    if (v == nodata || v <= -1e30 || v >= 1e30 || double.IsNaN(v)) continue;
                    reducer.Add(bx + x, by + y, v);
                }
        }

        // Where it is: the pixel size and a point tied to the Earth; the key directory says degrees or a projection's units.
        var scale = Doubles(33550);
        var tie = Doubles(33922);
        var transform = Doubles(34264);
        var keys = Values(34735);
        int modelType = 0, linearUnits = 9001, verticalUnits = 0;
        for (int k = 4; k + 3 < keys.Length; k += 4)
        {
            int id = (int)keys[k], loc = (int)keys[k + 1], val = (int)keys[k + 3];
            if (loc != 0) continue;
            if (id == 1024) modelType = val;
            else if (id == 3076) linearUnits = val;
            else if (id == 4099) verticalUnits = val;
        }
        double sx = 0, sy = 0, originX = double.NaN, originY = double.NaN;
        if (scale.Length >= 2) { sx = scale[0]; sy = scale[1]; }
        if (tie.Length >= 6) { originX = tie[3] - tie[0] * sx; originY = tie[4] + tie[1] * sy; }
        if (transform.Length >= 16 && sx == 0) { sx = Math.Abs(transform[0]); sy = Math.Abs(transform[5]); originX = transform[3]; originY = transform[7]; }
        bool known = sx > 0 && sy > 0;
        bool degrees = modelType == 2 || modelType == 0 && known && sx < 0.1 && Math.Abs(originX) <= 360 && Math.Abs(originY) <= 90;
        double? lat = null, lon = null;
        double dx = 1, dy = 1;
        if (known && degrees)
        {
            lat = originY - sy * h / 2;
            lon = originX + sx * w / 2;
            dy = sy * MetresPerDegree;
            dx = sx * MetresPerDegree * Math.Cos(lat.Value * Math.PI / 180);
        }
        else if (known)
        {
            double unit = linearUnits switch { 9002 => 0.3048, 9003 => 1200.0 / 3937, _ => 1 };
            dx = sx * unit;
            dy = sy * unit;
        }
        var units = verticalUnits is 9002 or 9003 || !degrees && linearUnits is 9002 or 9003 && verticalUnits == 0 ? HeightUnits.Feet : HeightUnits.Metres;
        if (!known) { dx = dy = 10; }
        return reducer.Grid(dx, dy) with
        {
            Georeferenced = known,
            Units = units,
            Lat = lat,
            Lon = lon,
            Name = name,
            Source = "GeoTIFF file",
        };
    }

    internal const double MetresPerDegree = 6371008.8 * Math.PI / 180;

    static double Sample(ReadOnlySpan<byte> s, int format, int bits, bool le) => (format, bits) switch
    {
        (3, 32) => le ? BinaryPrimitives.ReadSingleLittleEndian(s) : BinaryPrimitives.ReadSingleBigEndian(s),
        (3, 64) => le ? BinaryPrimitives.ReadDoubleLittleEndian(s) : BinaryPrimitives.ReadDoubleBigEndian(s),
        (2, 8) => (sbyte)s[0],
        (2, 16) => le ? BinaryPrimitives.ReadInt16LittleEndian(s) : BinaryPrimitives.ReadInt16BigEndian(s),
        (2, 32) => le ? BinaryPrimitives.ReadInt32LittleEndian(s) : BinaryPrimitives.ReadInt32BigEndian(s),
        (_, 8) => s[0],
        (_, 16) => le ? BinaryPrimitives.ReadUInt16LittleEndian(s) : BinaryPrimitives.ReadUInt16BigEndian(s),
        (_, 32) => le ? BinaryPrimitives.ReadUInt32LittleEndian(s) : BinaryPrimitives.ReadUInt32BigEndian(s),
        _ => double.NaN,
    };

    static byte[] Decompress(ReadOnlySpan<byte> src, int compression, int expected)
    {
        switch (compression)
        {
            case 1: return src.ToArray();
            case 8 or 32946:
                {
                    using var z = new ZLibStream(new MemoryStream(src.ToArray()), CompressionMode.Decompress);
                    var outBuf = new byte[expected];
                    int got = 0;
                    while (got < expected) { int k = z.Read(outBuf, got, expected - got); if (k == 0) break; got += k; }
                    return outBuf;
                }
            case 32773: return PackBits(src, expected);
            default: return Lzw(src.ToArray(), expected);
        }
    }

    static byte[] PackBits(ReadOnlySpan<byte> src, int expected)
    {
        var o = new byte[expected];
        int i = 0, p = 0;
        while (i < src.Length && p < expected)
        {
            int c = (sbyte)src[i++];
            if (c >= 0) { for (int k = 0; k <= c && i < src.Length && p < expected; k++) o[p++] = src[i++]; }
            else if (c != -128 && i < src.Length) { byte b = src[i++]; for (int k = 0; k <= -c && p < expected; k++) o[p++] = b; }
        }
        return o;
    }

    /// <summary>TIFF's LZW: codes most significant bit first, 9 to 12 bits, widening one code early.</summary>
    internal static byte[] Lzw(byte[] src, int expected)
    {
        var o = new byte[expected];
        int p = 0;
        var prefix = new int[4096];
        var suffix = new byte[4096];
        var length = new int[4096];
        for (int i = 0; i < 256; i++) { prefix[i] = -1; suffix[i] = (byte)i; length[i] = 1; }
        int next = 258, width = 9, old = -1;
        long bitPos = 0, totalBits = src.Length * 8L;
        var stack = new byte[4096];
        int Read()
        {
            if (bitPos + width > totalBits) return 257;
            int v = 0;
            for (int k = 0; k < width; k++)
            {
                long b = bitPos + k;
                v = (v << 1) | ((src[(int)(b >> 3)] >> (7 - (int)(b & 7))) & 1);
            }
            bitPos += width;
            return v;
        }
        // Write a code's bytes and return its first byte.
        byte Emit(int code)
        {
            int len = length[code], c = code;
            for (int k = len - 1; k >= 0; k--) { stack[k] = suffix[c]; c = prefix[c]; }
            for (int k = 0; k < len && p < expected; k++) o[p++] = stack[k];
            return stack[0];
        }
        while (p < expected)
        {
            int code = Read();
            if (code == 257) break;
            if (code == 256)
            {
                next = 258; width = 9; old = -1;
                continue;
            }
            if (old < 0)
            {
                if (code > 255) break;
                Emit(code);
                old = code;
                continue;
            }
            byte first;
            if (code < next)
            {
                first = Emit(code);
                Add(old, first);
            }
            else
            {
                // The code being made right now: the old string plus its own first byte.
                int len = length[old], c = old;
                for (int k = len - 1; k >= 0; k--) { stack[k] = suffix[c]; c = prefix[c]; }
                first = stack[0];
                Add(old, first);
                Emit(next - 1);
            }
            old = code;
        }
        return o;

        void Add(int pre, byte b)
        {
            if (next >= 4096) return;
            prefix[next] = pre;
            suffix[next] = b;
            length[next] = length[pre] + 1;
            next++;
            if (next + 1 >= 1 << width && width < 12) width++;
        }
    }

    /// <summary>Predictor 2: each sample was stored as the difference from the one to its left.</summary>
    static void UndoDifferences(byte[] b, int rowBytes, int rows, int stride, int bytesPer, bool le)
    {
        for (int y = 0; y < rows; y++)
        {
            int r = y * rowBytes;
            int count = rowBytes / bytesPer;
            for (int i = stride; i < count; i++)
            {
                int at = r + i * bytesPer, prev = r + (i - stride) * bytesPer;
                if (at + bytesPer > b.Length) break;
                switch (bytesPer)
                {
                    case 1: b[at] += b[prev]; break;
                    case 2:
                        {
                            ushort v = (ushort)(Get16(b, at, le) + Get16(b, prev, le));
                            Put16(b, at, v, le);
                            break;
                        }
                    case 4:
                        {
                            uint v = Get32(b, at, le) + Get32(b, prev, le);
                            Put32(b, at, v, le);
                            break;
                        }
                }
            }
        }
    }

    static ushort Get16(byte[] b, int at, bool le) => le ? BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(at)) : BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(at));
    static void Put16(byte[] b, int at, ushort v, bool le) { if (le) BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(at), v); else BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(at), v); }
    static uint Get32(byte[] b, int at, bool le) => le ? BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at)) : BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(at));
    static void Put32(byte[] b, int at, uint v, bool le) { if (le) BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(at), v); else BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(at), v); }

    /// <summary>
    /// Predictor 3 (floating point): each row's bytes were split into planes, most significant
    /// first, and differenced byte by byte. Put back, the samples come out in this computer's order.
    /// </summary>
    static void UndoFloatPredictor(byte[] b, int samplesPerRow, int rows, int bytesPer, bool _)
    {
        int rowBytes = samplesPerRow * bytesPer;
        var tmp = new byte[rowBytes];
        for (int y = 0; y < rows; y++)
        {
            int r = y * rowBytes;
            if (r + rowBytes > b.Length) break;
            for (int i = 1; i < rowBytes; i++) b[r + i] += b[r + i - 1];
            Buffer.BlockCopy(b, r, tmp, 0, rowBytes);
            for (int s = 0; s < samplesPerRow; s++)
                for (int k = 0; k < bytesPer; k++)
                    b[r + s * bytesPer + k] = BitConverter.IsLittleEndian ? tmp[(bytesPer - k - 1) * samplesPerRow + s] : tmp[k * samplesPerRow + s];
        }
    }
}
