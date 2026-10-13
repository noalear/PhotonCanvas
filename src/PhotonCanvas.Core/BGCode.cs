using System.Text;

namespace PhotonCanvas.Core;

/// <summary>
/// Decoder for xTool's tokenized binary G-code (<c>motion.bgcode</c>).
/// <list type="bullet">
/// <item>lines end with <c>0x0A</c></item>
/// <item><c>0xC3</c> starts a comment; the rest of the line is UTF-8 (<c># ...</c>)</item>
/// <item><c>0xE1..0xFA</c> are the letters A–Z</item>
/// <item><c>0xC0</c> space, <c>0xCE</c> <c>.</c>, <c>0xCD</c> <c>-</c>, <c>0xC2</c> <c>"</c></item>
/// <item>other bytes are packed BCD digit pairs (<c>0x43</c> → <c>43</c>)</item>
/// </list>
/// Numbers come out zero-padded (<c>X0143.6740</c>); <see cref="Tidy"/> normalises them.
/// </summary>
public static class BGCode
{
    /// <summary>Raw decode. Unknown bytes appear as <c>&lt;xx&gt;</c> so new tokens stand out.</summary>
    public static string Decode(ReadOnlySpan<byte> data)
    {
        var o = new StringBuilder(data.Length * 2);
        int i = 0;
        while (i < data.Length)
        {
            byte b = data[i];
            switch (b)
            {
                case 0x0A: o.Append('\n'); break;
                case 0xC3:
                    int end = data[i..].IndexOf((byte)0x0A);
                    end = end < 0 ? data.Length : i + end;
                    o.Append('#').Append(Encoding.UTF8.GetString(data[(i + 1)..end]));
                    i = end;
                    continue;
                case >= 0xE1 and <= 0xFA: o.Append((char)('A' + (b - 0xE1))); break;
                case 0xC0: o.Append(' '); break;
                case 0xCE: o.Append('.'); break;
                case 0xCD: o.Append('-'); break;
                case 0xC2: o.Append('"'); break;
                default:
                    if ((b >> 4) <= 9 && (b & 0xF) <= 9)
                        o.Append((char)('0' + (b >> 4))).Append((char)('0' + (b & 0xF)));
                    else
                        o.Append($"<{b:x2}>");
                    break;
            }
            i++;
        }
        return o.ToString();
    }

    /// <summary>
    /// Strip BCD padding from every number outside comments (including inside
    /// quoted <c>G198</c> sub-commands): <c>X0143.6740</c> → <c>X143.674</c>, <c>G00</c> → <c>G0</c>.
    /// </summary>
    public static string Tidy(string text) => string.Join("\n", text.Split('\n').Select(TidyLine));

    static string TidyLine(string line)
    {
        if (line.StartsWith('#')) return line;
        var o = new StringBuilder(line.Length);
        int i = 0;
        while (i < line.Length)
        {
            char c = line[i++];
            o.Append(c);
            if (c is >= 'A' and <= 'Z')
            {
                int start = i;
                while (i < line.Length && (char.IsAsciiDigit(line[i]) || line[i] == '.' || line[i] == '-')) i++;
                if (i > start) o.Append(Normalise(line[start..i]));
            }
        }
        return o.ToString();
    }

    static string Normalise(string num)
    {
        bool neg = num.StartsWith('-');
        var n = neg ? num[1..] : num;
        string body;
        int dot = n.IndexOf('.');
        if (dot >= 0)
        {
            var whole = n[..dot].TrimStart('0');
            var frac = n[(dot + 1)..].TrimEnd('0');
            if (whole.Length == 0) whole = "0";
            body = frac.Length == 0 ? whole : $"{whole}.{frac}";
        }
        else
        {
            body = n.TrimStart('0');
            if (body.Length == 0) body = "0";
        }
        return neg && body != "0" ? "-" + body : body;
    }
}
