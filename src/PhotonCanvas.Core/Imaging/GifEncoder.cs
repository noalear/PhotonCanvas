namespace PhotonCanvas.Core.Imaging;

/// <summary>
/// Animated GIF writer: frames of the same size, looping forever. Each frame gets its own
/// 256-colour palette (median cut), which keeps photos of coloured metal faithful enough to compare.
/// </summary>
public static class GifEncoder
{
    /// <param name="frames">Frames, all the same size.</param>
    /// <param name="delayCs">How long each frame shows, in hundredths of a second.</param>
    public static byte[] Encode(IReadOnlyList<RasterImage> frames, int delayCs)
    {
        if (frames.Count == 0) throw new ArgumentException("No frames.");
        int w = frames[0].Width, h = frames[0].Height;
        if (frames.Any(f => f.Width != w || f.Height != h)) throw new ArgumentException("Frames must all be the same size.");
        if (w > 65535 || h > 65535) throw new ArgumentException("Too big for a GIF.");
        using var o = new MemoryStream();
        void U16(int v) { o.WriteByte((byte)(v & 0xFF)); o.WriteByte((byte)(v >> 8 & 0xFF)); }
        o.Write("GIF89a"u8);
        U16(w);
        U16(h);
        o.WriteByte(0x70); // no global colour table, 8-bit colour resolution
        o.WriteByte(0);
        o.WriteByte(0);
        // Loop forever (NETSCAPE2.0 application extension).
        o.Write([0x21, 0xFF, 0x0B]);
        o.Write("NETSCAPE2.0"u8);
        o.Write([0x03, 0x01, 0x00, 0x00, 0x00]);
        foreach (var f in frames)
        {
            var (palette, indices) = Quantize(f);
            // Graphic control: replace the previous frame, this long.
            o.Write([0x21, 0xF9, 0x04, 0x04]);
            U16(Math.Clamp(delayCs, 2, 65535));
            o.Write([0x00, 0x00]);
            // Image descriptor with a 256-entry local colour table.
            o.WriteByte(0x2C);
            U16(0); U16(0); U16(w); U16(h);
            o.WriteByte(0x87);
            o.Write(palette);
            o.WriteByte(8); // LZW minimum code size
            WriteSubBlocks(o, Lzw(indices));
        }
        o.WriteByte(0x3B);
        return o.ToArray();
    }

    /// <summary>
    /// A 256-colour palette by median cut over the picture's colours (5 bits a channel): the
    /// colour space is split again and again at the median of its widest box, so smooth
    /// gradients keep their steps small. Then each pixel's nearest palette colour.
    /// </summary>
    static (byte[] Palette, byte[] Indices) Quantize(RasterImage f)
    {
        var count = new int[32768];
        var px = f.Rgba;
        int n = f.Width * f.Height;
        for (int i = 0; i < n; i++) count[Key(px, i * 4)]++;
        var used = Enumerable.Range(0, 32768).Where(k => count[k] > 0).ToList();
        static int Ch(int k, int c) => c == 0 ? k >> 10 & 31 : c == 1 ? k >> 5 & 31 : k & 31;
        var boxes = new List<List<int>> { used };
        while (boxes.Count < 256)
        {
            // The box with the widest spread of any channel (that can still be split).
            int bi = -1, bc = 0, bw = 0;
            for (int b = 0; b < boxes.Count; b++)
            {
                if (boxes[b].Count < 2) continue;
                for (int c = 0; c < 3; c++)
                {
                    int lo = 31, hi = 0;
                    foreach (var k in boxes[b]) { int v = Ch(k, c); if (v < lo) lo = v; if (v > hi) hi = v; }
                    if (hi - lo > bw) { bw = hi - lo; bi = b; bc = c; }
                }
            }
            if (bi < 0) break;
            var box = boxes[bi];
            box.Sort((a, b) => Ch(a, bc).CompareTo(Ch(b, bc)));
            // Split where half the pixels fall on each side.
            long total = box.Sum(k => (long)count[k]), run = 0;
            int cut = 1;
            for (int i = 0; i < box.Count - 1; i++)
            {
                run += count[box[i]];
                if (run * 2 >= total) { cut = i + 1; break; }
            }
            boxes[bi] = box.GetRange(0, cut);
            boxes.Add(box.GetRange(cut, box.Count - cut));
        }
        var palette = new byte[256 * 3];
        for (int b = 0; b < boxes.Count; b++)
        {
            double r = 0, g = 0, bl = 0, w = 0;
            foreach (var k in boxes[b])
            {
                double c = count[k];
                r += Ch(k, 0) * c; g += Ch(k, 1) * c; bl += Ch(k, 2) * c; w += c;
            }
            if (w <= 0) continue;
            palette[b * 3] = (byte)Math.Round(r / w * 255 / 31);
            palette[b * 3 + 1] = (byte)Math.Round(g / w * 255 / 31);
            palette[b * 3 + 2] = (byte)Math.Round(bl / w * 255 / 31);
        }
        int colours = Math.Max(1, boxes.Count);
        // Nearest palette colour for every used 5-bit colour, worked out once.
        var nearest = new short[32768];
        Array.Fill(nearest, (short)-1);
        var indices = new byte[n];
        for (int i = 0; i < n; i++)
        {
            int k = Key(px, i * 4);
            if (nearest[k] < 0)
            {
                int r = Ch(k, 0) * 255 / 31, g = Ch(k, 1) * 255 / 31, b = Ch(k, 2) * 255 / 31;
                int best = 0, bestD = int.MaxValue;
                for (int p = 0; p < colours; p++)
                {
                    int dr = r - palette[p * 3], dg = g - palette[p * 3 + 1], db = b - palette[p * 3 + 2];
                    int d = 2 * dr * dr + 4 * dg * dg + 3 * db * db;
                    if (d < bestD) { bestD = d; best = p; }
                }
                nearest[k] = (short)best;
            }
            indices[i] = (byte)nearest[k];
        }
        return (palette, indices);
    }

    /// <summary>A pixel's colour at 5 bits a channel (transparent pixels count as black).</summary>
    static int Key(byte[] px, int i) => px[i + 3] < 128 ? 0 : (px[i] >> 3) << 10 | (px[i + 1] >> 3) << 5 | px[i + 2] >> 3;

    /// <summary>GIF-flavoured LZW of 8-bit indices: variable code width from 9 to 12 bits, clearing when the table fills.</summary>
    static byte[] Lzw(byte[] data)
    {
        const int Clear = 256, End = 257;
        var output = new List<byte>(data.Length / 2);
        int bitBuf = 0, bitCount = 0, width = 9, next = 258;
        var table = new Dictionary<int, int>(4096);
        void Emit(int code)
        {
            bitBuf |= code << bitCount;
            bitCount += width;
            while (bitCount >= 8) { output.Add((byte)(bitBuf & 0xFF)); bitBuf >>= 8; bitCount -= 8; }
        }
        Emit(Clear);
        int prefix = data.Length > 0 ? data[0] : 0;
        for (int i = 1; i < data.Length; i++)
        {
            int c = data[i], key = prefix << 8 | c;
            if (table.TryGetValue(key, out int code)) { prefix = code; continue; }
            Emit(prefix);
            if (next < 4096)
            {
                table[key] = next++;
                // The decoder widens one code later than the encoder adds the entry.
                if (next > (1 << width) && width < 12) width++;
            }
            else
            {
                Emit(Clear);
                table.Clear();
                next = 258;
                width = 9;
            }
            prefix = c;
        }
        if (data.Length > 0) Emit(prefix);
        Emit(End);
        if (bitCount > 0) output.Add((byte)(bitBuf & 0xFF));
        return [.. output];
    }

    static void WriteSubBlocks(Stream o, byte[] data)
    {
        for (int i = 0; i < data.Length; i += 255)
        {
            int len = Math.Min(255, data.Length - i);
            o.WriteByte((byte)len);
            o.Write(data, i, len);
        }
        o.WriteByte(0);
    }
}
