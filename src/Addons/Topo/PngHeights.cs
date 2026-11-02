using System.Buffers.Binary;
using System.IO.Compression;

namespace PhotonCanvas.Addon.Topo;

/// <summary>
/// Just enough of PNG to read heights exactly: 8 or 16 bits a channel, grey or colour, no
/// colour management or gamma (an image library would "correct" the values and spoil them).
/// </summary>
static class PngHeights
{
    static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    /// <summary>The picture's samples: width, height, channels (1 grey, 2 grey+alpha, 3 RGB, 4 RGBA), bits a channel, and the values (0..255 or 0..65535), row by row.</summary>
    public sealed record Image(int Width, int Height, int Channels, int Bits, ushort[] Samples)
    {
        public int At(int x, int y, int c) => Samples[(y * Width + x) * Channels + c];
    }

    public static bool IsPng(ReadOnlySpan<byte> data) => data.Length >= 8 && data[..8].SequenceEqual(Signature);

    public static Image Decode(byte[] data)
    {
        if (!IsPng(data)) throw new FormatException("It isn't a PNG file.");
        int pos = 8, w = 0, h = 0, bits = 0, type = -1, interlace = 0;
        byte[]? palette = null;
        using var idat = new MemoryStream();
        while (pos + 8 <= data.Length)
        {
            int len = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(pos));
            string kind = System.Text.Encoding.ASCII.GetString(data, pos + 4, 4);
            if (len < 0 || pos + 12 + len > data.Length) throw new FormatException("The PNG file is cut short.");
            var body = data.AsSpan(pos + 8, len);
            switch (kind)
            {
                case "IHDR":
                    w = BinaryPrimitives.ReadInt32BigEndian(body);
                    h = BinaryPrimitives.ReadInt32BigEndian(body[4..]);
                    bits = body[8];
                    type = body[9];
                    interlace = body[12];
                    break;
                case "PLTE": palette = body.ToArray(); break;
                case "IDAT": idat.Write(body); break;
            }
            pos += 12 + len;
            if (kind == "IEND") break;
        }
        if (w <= 0 || h <= 0 || (long)w * h > 200_000_000) throw new FormatException("The PNG file has no picture in it, or one too big to read.");
        if (interlace != 0) throw new FormatException("The PNG is saved interlaced: save it again without interlacing.");
        int channels = type switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => throw new FormatException("The PNG's kind of colour isn't one this reads.") };
        if (type == 3 ? bits is not (1 or 2 or 4 or 8) : bits is not (8 or 16))
            throw new FormatException($"The PNG has {bits} bits a channel: save it with 8 or 16.");
        int bpp = Math.Max(1, channels * bits / 8);
        int stride = (w * channels * bits + 7) / 8;
        var raw = new byte[(long)(stride + 1) * h];
        idat.Position = 0;
        using (var z = new ZLibStream(idat, CompressionMode.Decompress))
        {
            int got = 0;
            while (got < raw.Length)
            {
                int n = z.Read(raw, got, raw.Length - got);
                if (n == 0) throw new FormatException("The PNG file's picture is cut short.");
                got += n;
            }
        }
        // Undo each row's filter.
        var prev = new byte[stride];
        var cur = new byte[stride];
        var outBits = type == 3 ? 8 : bits;
        int outCh = type == 3 ? 3 : channels;
        var samples = new ushort[(long)w * h * outCh];
        for (int y = 0; y < h; y++)
        {
            int f = raw[(long)y * (stride + 1)];
            Buffer.BlockCopy(raw, y * (stride + 1) + 1, cur, 0, stride);
            for (int i = 0; i < stride; i++)
            {
                int a = i >= bpp ? cur[i - bpp] : 0, b = prev[i], c = i >= bpp ? prev[i - bpp] : 0;
                cur[i] = (byte)(cur[i] + f switch
                {
                    0 => 0,
                    1 => a,
                    2 => b,
                    3 => (a + b) >> 1,
                    4 => Paeth(a, b, c),
                    _ => throw new FormatException("The PNG file's picture is damaged."),
                });
            }
            long o = (long)y * w * outCh;
            if (type == 3)
            {
                if (palette is null) throw new FormatException("The PNG has no palette.");
                for (int x = 0; x < w; x++)
                {
                    int bit = x * bits, idx = (cur[bit >> 3] >> (8 - bits - (bit & 7))) & ((1 << bits) - 1);
                    for (int k = 0; k < 3; k++) samples[o + x * 3 + k] = 3 * idx + k < palette.Length ? palette[3 * idx + k] : (ushort)0;
                }
            }
            else if (bits == 16)
                for (int i = 0; i < w * channels; i++) samples[o + i] = (ushort)(cur[2 * i] << 8 | cur[2 * i + 1]);
            else
                for (int i = 0; i < w * channels; i++) samples[o + i] = cur[i];
            (prev, cur) = (cur, prev);
        }
        return new Image(w, h, outCh, outBits, samples);
    }

    static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    /// <summary>Terrarium tiles: metres = red × 256 + green + blue / 256 − 32768.</summary>
    public static double Terrarium(int r, int g, int b) => r * 256 + g + b / 256.0 - 32768;

    /// <summary>Mapbox terrain-RGB: metres = −10000 + (red × 65536 + green × 256 + blue) × 0.1.</summary>
    public static double TerrainRgb(int r, int g, int b) => -10000 + (r * 65536 + g * 256 + b) * 0.1;
}
