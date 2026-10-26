using System.Text;

namespace PhotonCanvas.Core;

/// <summary>
/// A job's G-code as UTF-8 lines joined by <c>\n</c>, kept in chunks: a dense wobble on long cuts
/// makes more text than one string can hold (a billion characters, however much memory the PC
/// has), so the lines are written straight into bytes as they're made.
/// </summary>
public sealed class GcodeText
{
    const int ChunkBytes = 16 << 20;
    readonly List<byte[]> _chunks = [];
    byte[] _current = new byte[4096];
    int _used;
    bool _first = true;
    string? _text;

    public GcodeText() { }
    public GcodeText(IEnumerable<string> lines) => AddRange(lines);

    /// <summary>Bytes so far.</summary>
    public long Length { get; private set; }
    /// <summary>Lines so far.</summary>
    public long Lines { get; private set; }

    public void Add(string line)
    {
        _text = null;
        if (!_first) Put("\n"u8);
        _first = false;
        Lines++;
        int n = Encoding.UTF8.GetMaxByteCount(line.Length);
        if (n <= 1024)
        {
            Span<byte> buf = stackalloc byte[n];
            Put(buf[..Encoding.UTF8.GetBytes(line, buf)]);
        }
        else Put(Encoding.UTF8.GetBytes(line));
    }

    public void AddRange(IEnumerable<string> lines)
    {
        foreach (var l in lines) Add(l);
    }

    void Put(ReadOnlySpan<byte> bytes)
    {
        Length += bytes.Length;
        while (bytes.Length > 0)
        {
            if (_used == _current.Length)
            {
                if (_current.Length < ChunkBytes) Array.Resize(ref _current, Math.Min(ChunkBytes, _current.Length * 4));
                else
                {
                    _chunks.Add(_current);
                    _current = new byte[ChunkBytes];
                    _used = 0;
                }
            }
            int k = Math.Min(bytes.Length, _current.Length - _used);
            bytes[..k].CopyTo(_current.AsSpan(_used));
            _used += k;
            bytes = bytes[k..];
        }
    }

    /// <summary>Writes all the bytes to <paramref name="to"/> at <paramref name="at"/>.</summary>
    public void CopyTo(byte[] to, long at)
    {
        foreach (var c in _chunks)
        {
            Array.Copy(c, 0, to, at, c.Length);
            at += c.Length;
        }
        Array.Copy(_current, 0, to, at, _used);
    }

    public void WriteTo(Stream s)
    {
        foreach (var c in _chunks) s.Write(c);
        s.Write(_current, 0, _used);
    }

    /// <summary>The text as one string, for small jobs (tests, previews); a huge job can't be one.</summary>
    public override string ToString()
    {
        if (_text is not null) return _text;
        if (Length > int.MaxValue / 2) throw new JobException($"The job's G-code is {Length / 1e9:0.0} GB, too big to show as text. Save it to a file instead.");
        var all = new byte[Length];
        CopyTo(all, 0);
        return _text = Encoding.UTF8.GetString(all);
    }
}
