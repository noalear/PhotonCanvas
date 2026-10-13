"""V2 WebSocket framing: 0xBABE magic, 3-byte length, type, body CRC16, header CRC16.

See docs/protocol-f2-ultra.md.
"""
from enum import IntEnum

MAGIC = b"\xba\xbe"
HEADER_LEN = 10


class ProtocolType(IntEnum):
    F0F7 = 1
    F3F4 = 2
    F8F9 = 3
    JSON = 4
    BUFFER = 5
    NETWORK_CONFIG = 32
    FILE_TRANSFER = 33
    MEDIA_STREAM = 34


def _make_table():
    table = []
    for i in range(256):
        c = i
        for _ in range(8):
            c = (c >> 1) ^ 0xA001 if c & 1 else c >> 1
        table.append(c)
    return table


_TABLE = _make_table()


def crc16(data: bytes) -> int:
    """CRC-16/ARC (reflected poly 0xA001, init 0)."""
    crc = 0
    for b in data:
        crc = (_TABLE[(crc ^ b) & 0xFF] ^ (crc >> 8)) & 0xFFFF
    return crc


def encode(payload: bytes | str, ptype: int, body_crc: bool = True) -> bytes:
    if isinstance(payload, str):
        payload = payload.encode("utf-8")
    head = bytearray(HEADER_LEN)
    head[0:2] = MAGIC
    head[2:5] = len(payload).to_bytes(3, "big")
    head[5] = (ptype & 0x7F) | (0 if body_crc else 0x80)
    head[6:8] = (crc16(payload) if body_crc else 0).to_bytes(2, "big")
    head[8:10] = crc16(bytes(head[:8])).to_bytes(2, "big")
    return bytes(head) + payload


class FrameDecoder:
    """Accumulates bytes and yields (protocol_type, payload) for each valid frame."""

    def __init__(self, skip_body_crc_types=()):
        self.buf = bytearray()
        self.skip = set(skip_body_crc_types)

    def feed(self, data: bytes):
        self.buf += data
        out = []
        i = 0
        n = len(self.buf)
        while i + HEADER_LEN <= n:
            if self.buf[i:i + 2] != MAGIC:
                i += 1
                continue
            length = int.from_bytes(self.buf[i + 2:i + 5], "big")
            total = HEADER_LEN + length
            if i + total > n:
                break
            if crc16(bytes(self.buf[i:i + 8])) != int.from_bytes(self.buf[i + 8:i + 10], "big"):
                i += 1
                continue
            raw_type = self.buf[i + 5]
            ptype = raw_type & 0x7F
            body = bytes(self.buf[i + HEADER_LEN:i + total])
            if not (raw_type & 0x80 or ptype in self.skip):
                if crc16(body) != int.from_bytes(self.buf[i + 6:i + 8], "big"):
                    i += 1
                    continue
            out.append((ptype, body))
            i += total
        del self.buf[:i]
        return out
