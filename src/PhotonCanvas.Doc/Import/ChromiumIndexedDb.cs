using System.Buffers.Binary;
using System.Text;

namespace PhotonCanvas.Doc.Import;

/// <summary>
/// Reads a Chromium (Electron) IndexedDB folder, read-only: its LevelDB tables and log, Snappy
/// blocks, IndexedDB's key prefixes and the values in V8's serialization format. Values come back
/// as plain .NET data: <see cref="Dictionary{TKey, TValue}"/> (string keys) for objects and maps,
/// <see cref="List{T}"/> for arrays, <see cref="string"/>, <see cref="double"/>, <see cref="bool"/>, or null.
/// Enough of V8's format for app data (no class instances, typed arrays are skipped).
/// </summary>
public static class ChromiumIndexedDb
{
    /// <summary>One object store's records: (database id, store id) → values.</summary>
    public static Dictionary<(long Db, long Store), List<object?>> Read(string folder)
    {
        var latest = new Dictionary<string, (ulong Seq, bool Put, byte[]? Value)>(StringComparer.Ordinal);
        void Keep(byte[] key, ulong seq, bool put, byte[]? value)
        {
            string k = Convert.ToBase64String(key);
            if (!latest.TryGetValue(k, out var had) || had.Seq < seq) latest[k] = (seq, put, value);
        }
        foreach (var f in Directory.EnumerateFiles(folder, "*.ldb").Order())
            foreach (var (key, seq, put, value) in Table(File.ReadAllBytes(f))) Keep(key, seq, put, value);
        foreach (var f in Directory.EnumerateFiles(folder, "*.log").Order())
            foreach (var (key, seq, put, value) in Log(File.ReadAllBytes(f))) Keep(key, seq, put, value);
        var o = new Dictionary<(long, long), List<object?>>();
        foreach (var (k, (_, put, value)) in latest)
        {
            if (!put || value is null) continue;
            var key = Convert.FromBase64String(k);
            if (Prefix(key) is not var (db, store, index) || db <= 0 || index != 1) continue; // index 1: the records themselves
            object? v;
            try { v = Value(value); }
            catch (Exception e) when (e is FormatException or IndexOutOfRangeException or ArgumentException) { continue; }
            if (!o.TryGetValue((db, store), out var list)) o[(db, store)] = list = [];
            list.Add(v);
        }
        return o;
    }

    /// <summary>Object store names: (database id, store id) → name, from the metadata records.</summary>
    public static Dictionary<(long Db, long Store), string> StoreNames(string folder)
    {
        var o = new Dictionary<(long, long), string>();
        var all = new List<(byte[] Key, ulong Seq, bool Put, byte[]? Value)>();
        foreach (var f in Directory.EnumerateFiles(folder, "*.ldb").Order()) all.AddRange(Table(File.ReadAllBytes(f)));
        foreach (var f in Directory.EnumerateFiles(folder, "*.log").Order()) all.AddRange(Log(File.ReadAllBytes(f)));
        foreach (var (key, _, put, value) in all.OrderBy(x => x.Seq))
        {
            if (!put || value is null || Prefix(key) is not var (db, store, index) || store != 0 || index != 0) continue;
            int at = PrefixLength(key);
            // (db, 0, 0) 50 <store id> 0 → the store's name, UTF-16 big-endian.
            if (at >= key.Length || key[at] != 50) continue;
            int i = at + 1;
            long sid = (long)Varint(key, ref i);
            if (i < key.Length && key[i] == 0 && value.Length % 2 == 0) o[(db, sid)] = Encoding.BigEndianUnicode.GetString(value);
        }
        return o;
    }

    // ---- LevelDB ---------------------------------------------------------------------------------

    static ulong Varint(byte[] b, ref int i)
    {
        ulong r = 0;
        for (int s = 0; ; s += 7)
        {
            byte c = b[i++];
            r |= (ulong)(c & 0x7F) << s;
            if (c < 0x80) return r;
        }
    }

    /// <summary>A table file's entries: user key, sequence number, put (or delete), value.</summary>
    public static IEnumerable<(byte[] Key, ulong Seq, bool Put, byte[]? Value)> Table(byte[] d)
    {
        if (d.Length < 48) yield break;
        int f = d.Length - 48;
        Varint(d, ref f); Varint(d, ref f); // the meta-index handle
        long io = (long)Varint(d, ref f), isz = (long)Varint(d, ref f);
        foreach (var (_, handle) in Entries(Block(d, io, isz)))
        {
            int h = 0;
            long off = (long)Varint(handle, ref h), size = (long)Varint(handle, ref h);
            foreach (var (key, value) in Entries(Block(d, off, size)))
            {
                if (key.Length < 8) continue;
                ulong tag = BinaryPrimitives.ReadUInt64LittleEndian(key.AsSpan(key.Length - 8));
                yield return (key[..^8], tag >> 8, (tag & 0xFF) == 1, value);
            }
        }
    }

    static byte[] Block(byte[] d, long off, long size)
    {
        var raw = d.AsSpan((int)off, (int)size);
        return d[off + size] == 1 ? Snappy(raw) : raw.ToArray();
    }

    static IEnumerable<(byte[] Key, byte[] Value)> Entries(byte[] blk)
    {
        int restarts = (int)BinaryPrimitives.ReadUInt32LittleEndian(blk.AsSpan(blk.Length - 4));
        int end = blk.Length - 4 - 4 * restarts, i = 0;
        var key = Array.Empty<byte>();
        while (i < end)
        {
            int shared = (int)Varint(blk, ref i), unshared = (int)Varint(blk, ref i), vlen = (int)Varint(blk, ref i);
            var k = new byte[shared + unshared];
            Array.Copy(key, k, shared);
            Array.Copy(blk, i, k, shared, unshared);
            i += unshared;
            key = k;
            yield return (k, blk[i..(i + vlen)]);
            i += vlen;
        }
    }

    /// <summary>A log file's writes, in order: 32 KiB blocks of records, each record a write batch.</summary>
    public static IEnumerable<(byte[] Key, ulong Seq, bool Put, byte[]? Value)> Log(byte[] d)
    {
        var batches = new List<byte[]>();
        var part = new List<byte>();
        int i = 0;
        while (i + 7 <= d.Length)
        {
            int left = 32768 - i % 32768;
            if (left < 7) { i += left; continue; }
            int len = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(i + 4)), type = d[i + 6];
            if (i + 7 + len > d.Length) break;
            var payload = d.AsSpan(i + 7, len);
            i += 7 + len;
            switch (type)
            {
                case 1: batches.Add(payload.ToArray()); break;          // whole
                case 2: part.Clear(); part.AddRange(payload.ToArray()); break; // first
                case 3: part.AddRange(payload.ToArray()); break;        // middle
                case 4: part.AddRange(payload.ToArray()); batches.Add([.. part]); part.Clear(); break; // last
            }
        }
        foreach (var r in batches)
        {
            if (r.Length < 12) continue;
            ulong seq = BinaryPrimitives.ReadUInt64LittleEndian(r);
            int count = (int)BinaryPrimitives.ReadUInt32LittleEndian(r.AsSpan(8)), j = 12;
            for (int k = 0; k < count && j < r.Length; k++)
            {
                byte t = r[j++];
                int kl = (int)Varint(r, ref j);
                var key = r[j..(j + kl)];
                j += kl;
                byte[]? value = null;
                if (t == 1)
                {
                    int vl = (int)Varint(r, ref j);
                    value = r[j..(j + vl)];
                    j += vl;
                }
                yield return (key, seq + (ulong)k, t == 1, value);
            }
        }
    }

    /// <summary>Snappy decompression (the raw format LevelDB uses for its blocks).</summary>
    public static byte[] Snappy(ReadOnlySpan<byte> b)
    {
        int i = 0;
        ulong n = 0;
        for (int s = 0; ; s += 7)
        {
            byte c = b[i++];
            n |= (ulong)(c & 0x7F) << s;
            if (c < 0x80) break;
        }
        var o = new byte[n];
        int at = 0;
        while (i < b.Length)
        {
            byte t = b[i++];
            int kind = t & 3, len, off;
            if (kind == 0)
            {
                len = t >> 2;
                if (len >= 60)
                {
                    int bytes = len - 59;
                    len = 0;
                    for (int k = 0; k < bytes; k++) len |= b[i++] << (8 * k);
                }
                len++;
                b.Slice(i, len).CopyTo(o.AsSpan(at));
                i += len;
                at += len;
                continue;
            }
            if (kind == 1) { len = ((t >> 2) & 7) + 4; off = ((t >> 5) << 8) | b[i++]; }
            else if (kind == 2) { len = (t >> 2) + 1; off = b[i] | b[i + 1] << 8; i += 2; }
            else { len = (t >> 2) + 1; off = b[i] | b[i + 1] << 8 | b[i + 2] << 16 | b[i + 3] << 24; i += 4; }
            if (off <= 0 || off > at) throw new FormatException("Snappy: a copy reaches before the start.");
            for (int k = 0; k < len; k++, at++) o[at] = o[at - off];
        }
        if (at != o.Length) throw new FormatException("Snappy: the length doesn't match.");
        return o;
    }

    // ---- IndexedDB ------------------------------------------------------------------------------

    /// <summary>A key's (database, object store, index) prefix; null when it isn't one.</summary>
    public static (long Db, long Store, long Index)? Prefix(byte[] k)
    {
        if (k.Length == 0) return null;
        int ld = ((k[0] >> 5) & 7) + 1, lo = ((k[0] >> 2) & 7) + 1, li = (k[0] & 3) + 1;
        if (k.Length < 1 + ld + lo + li) return null;
        long Read(int at, int len) { long v = 0; for (int j = 0; j < len; j++) v |= (long)k[at + j] << (8 * j); return v; }
        return (Read(1, ld), Read(1 + ld, lo), Read(1 + ld + lo, li));
    }

    static int PrefixLength(byte[] k) => 1 + ((k[0] >> 5) & 7) + 1 + ((k[0] >> 2) & 7) + 1 + (k[0] & 3) + 1;

    /// <summary>A record's value: IndexedDB's version, Blink's envelope, then V8's serialized value.</summary>
    public static object? Value(byte[] v)
    {
        int i = 0;
        Varint(v, ref i);
        if (v[i] == 0xFF)
        {
            i++;
            Varint(v, ref i);
            if (v[i] == 0xFE) i += 13; // (Blink 21+) the trailer's offset and size
        }
        return new V8(v, i).Read();
    }

    /// <summary>V8's value serializer, the parts app data uses.</summary>
    sealed class V8(byte[] b, int start)
    {
        int _i = start;

        ulong Var() => Varint(b, ref _i);

        public object? Read()
        {
            byte t;
            while (true)
            {
                t = b[_i++];
                if (t == 0xFF) { Var(); continue; } // version
                if (t == 0) continue;                // padding
                break;
            }
            switch ((char)t)
            {
                case 'o':
                    {
                        var o = new Dictionary<string, object?>();
                        while (b[_i] != '{') { var k = Read(); o[Text(k)] = Read(); }
                        _i++;
                        Var();
                        return o;
                    }
                case 'A':
                    {
                        int n = (int)Var();
                        var a = new List<object?>(n);
                        for (int j = 0; j < n; j++) a.Add(Read());
                        while (b[_i] != '$') { Read(); Read(); } // extra named properties
                        _i++;
                        Var(); Var();
                        return a;
                    }
                case 'a':
                    {
                        Var();
                        var o = new Dictionary<string, object?>();
                        while (b[_i] != '@') { var k = Read(); o[Text(k)] = Read(); }
                        _i++;
                        Var(); Var();
                        return o;
                    }
                case ';': // Map
                    {
                        var o = new Dictionary<string, object?>();
                        while (b[_i] != ':') { var k = Read(); o[Text(k)] = Read(); }
                        _i++;
                        Var();
                        return o;
                    }
                case '\'': // Set
                    {
                        var a = new List<object?>();
                        while (b[_i] != ',') a.Add(Read());
                        _i++;
                        Var();
                        return a;
                    }
                case '"': { int n = (int)Var(); var s = Encoding.Latin1.GetString(b, _i, n); _i += n; return s; }
                case 'c': { int n = (int)Var(); var s = Encoding.Unicode.GetString(b, _i, n); _i += n; return s; }
                case 'S': { int n = (int)Var(); var s = Encoding.UTF8.GetString(b, _i, n); _i += n; return s; }
                case 'I': { ulong z = Var(); return (double)((long)(z >> 1) ^ -(long)(z & 1)); }
                case 'U': return (double)Var();
                case 'N': { double d = BinaryPrimitives.ReadDoubleLittleEndian(b.AsSpan(_i)); _i += 8; return d; }
                case 'D': { double d = BinaryPrimitives.ReadDoubleLittleEndian(b.AsSpan(_i)); _i += 8; return d; }
                case 'T': return true;
                case 'F': return false;
                case '_':
                case '0':
                case '-': return null;
                case '^': Var(); return null; // a reference back to an earlier object: not needed here
                case 'B': { int n = (int)Var(); _i += n; return null; } // an ArrayBuffer
                case 'V': Var(); Var(); Var(); return null;            // a view on one
                default: throw new FormatException($"V8 tag 0x{t:X2} isn't handled.");
            }
        }

        static string Text(object? k) => k switch { string s => s, double d => d.ToString(System.Globalization.CultureInfo.InvariantCulture), _ => "" };
    }
}
