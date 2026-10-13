using System.Buffers.Binary;
using System.Text;

namespace PhotonCanvas.Device;

/// <summary>Protocol type byte of a V2 frame (bits 0..6; bit 7 means "no body CRC").</summary>
public enum ProtocolType : byte
{
    F0F7 = 1,
    F3F4 = 2,
    F8F9 = 3,
    Json = 4,
    Buffer = 5,
    NetworkConfig = 32,
    FileTransfer = 33,
    MediaStream = 34,
}

/// <summary>CRC-16/ARC: reflected polynomial 0xA001, initial value 0, no final xor.</summary>
public static class Crc16
{
    static readonly ushort[] Table = MakeTable();

    static ushort[] MakeTable()
    {
        var table = new ushort[256];
        for (int i = 0; i < 256; i++)
        {
            int c = i;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? (c >> 1) ^ 0xA001 : c >> 1;
            table[i] = (ushort)c;
        }
        return table;
    }

    public static ushort Compute(ReadOnlySpan<byte> data)
    {
        int crc = 0;
        foreach (byte b in data)
            crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return (ushort)crc;
    }
}

/// <summary>
/// V2 WebSocket framing: <c>0xBA 0xBE</c>, 3-byte big-endian payload length, type byte
/// (bit 7 = no body CRC), CRC16 of the payload, CRC16 of header bytes 0..7, payload.
/// </summary>
public static class Frame
{
    public const int HeaderLength = 10;
    public const int MaxPayloadLength = 0xFFFFFF;
    public const byte Magic0 = 0xBA;
    public const byte Magic1 = 0xBE;
    /// <summary>Set in the type byte when the frame carries no body CRC.</summary>
    public const byte NoBodyCrcFlag = 0x80;

    /// <summary>Encodes one frame. With <paramref name="bodyCrc"/> false, bit 7 of the type is set and the body CRC is 0.</summary>
    public static byte[] Encode(ReadOnlySpan<byte> payload, ProtocolType type, bool bodyCrc = true)
    {
        if (payload.Length > MaxPayloadLength)
            throw new ArgumentException($"Frame payload is {payload.Length} bytes; the maximum is {MaxPayloadLength}.", nameof(payload));
        var frame = new byte[HeaderLength + payload.Length];
        var head = frame.AsSpan(0, HeaderLength);
        head[0] = Magic0;
        head[1] = Magic1;
        head[2] = (byte)(payload.Length >> 16);
        head[3] = (byte)(payload.Length >> 8);
        head[4] = (byte)payload.Length;
        head[5] = (byte)(((byte)type & 0x7F) | (bodyCrc ? 0 : NoBodyCrcFlag));
        BinaryPrimitives.WriteUInt16BigEndian(head[6..], bodyCrc ? Crc16.Compute(payload) : (ushort)0);
        BinaryPrimitives.WriteUInt16BigEndian(head[8..], Crc16.Compute(head[..8]));
        payload.CopyTo(frame.AsSpan(HeaderLength));
        return frame;
    }

    /// <summary>Encodes a UTF-8 string payload (normally JSON, <see cref="ProtocolType.Json"/>).</summary>
    public static byte[] Encode(string payload, ProtocolType type = ProtocolType.Json, bool bodyCrc = true) =>
        Encode(Encoding.UTF8.GetBytes(payload), type, bodyCrc);
}

/// <summary>One decoded frame. <see cref="Type"/> has bit 7 stripped.</summary>
public readonly record struct DecodedFrame(ProtocolType Type, byte[] Payload);

/// <summary>
/// Streaming frame decoder. Feed it whatever arrives on the socket; it returns every complete,
/// valid frame and keeps partial data for the next call. On a bad magic or a CRC mismatch it
/// drops one byte and searches again (resync). The body CRC is not checked when bit 7 of the
/// type byte is set or the type is one of the configured skip types. Not thread-safe.
/// </summary>
public sealed class FrameDecoder(IEnumerable<ProtocolType>? skipBodyCrcTypes = null)
{
    readonly HashSet<ProtocolType> _skip = [.. skipBodyCrcTypes ?? []];
    byte[] _buf = new byte[4096];
    int _start;
    int _count;

    /// <summary>Bytes held back waiting for the rest of a frame (or more data to resync).</summary>
    public int BufferedBytes => _count;

    public List<DecodedFrame> Feed(ReadOnlySpan<byte> data)
    {
        Append(data);
        var output = new List<DecodedFrame>();
        int i = _start;
        int end = _start + _count;
        while (i + Frame.HeaderLength <= end)
        {
            var b = _buf.AsSpan();
            if (b[i] != Frame.Magic0 || b[i + 1] != Frame.Magic1)
            {
                i++;
                continue;
            }
            // Check the header CRC before waiting for the body, so garbage that happens to
            // start with the magic can't make us wait for megabytes that never come.
            if (Crc16.Compute(b.Slice(i, 8)) != BinaryPrimitives.ReadUInt16BigEndian(b[(i + 8)..]))
            {
                i++;
                continue;
            }
            int length = (b[i + 2] << 16) | (b[i + 3] << 8) | b[i + 4];
            int total = Frame.HeaderLength + length;
            if (i + total > end)
                break;
            byte rawType = b[i + 5];
            var type = (ProtocolType)(rawType & 0x7F);
            var body = b.Slice(i + Frame.HeaderLength, length);
            bool checkBody = (rawType & Frame.NoBodyCrcFlag) == 0 && !_skip.Contains(type);
            if (checkBody && Crc16.Compute(body) != BinaryPrimitives.ReadUInt16BigEndian(b[(i + 6)..]))
            {
                i++;
                continue;
            }
            output.Add(new DecodedFrame(type, body.ToArray()));
            i += total;
        }
        _count = end - i;
        _start = _count == 0 ? 0 : i;
        return output;
    }

    void Append(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return;
        if (_start + _count + data.Length > _buf.Length)
        {
            // Compact, then grow if still needed.
            int needed = _count + data.Length;
            byte[] target = needed > _buf.Length ? new byte[Math.Max(needed, _buf.Length * 2)] : _buf;
            System.Buffer.BlockCopy(_buf, _start, target, 0, _count);
            _buf = target;
            _start = 0;
        }
        data.CopyTo(_buf.AsSpan(_start + _count));
        _count += data.Length;
    }
}
