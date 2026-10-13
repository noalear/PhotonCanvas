import json
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "src"))

from ytool.discovery import COMMON_KEY, PRIMARY_KEY, decrypt, encrypt
from ytool.frame import FrameDecoder, ProtocolType, crc16, encode


def test_crc16_arc_check_value():
    assert crc16(b"123456789") == 0xBB3D  # CRC-16/ARC standard check value


def test_frame_roundtrip_and_split_delivery():
    msg = json.dumps({"type": "request", "url": "/v1/user/ping"})
    f = encode(msg, ProtocolType.JSON) + encode(b"\x02", ProtocolType.FILE_TRANSFER, body_crc=False)
    assert f[:2] == b"\xba\xbe"
    d = FrameDecoder()
    out = d.feed(f[:7]) + d.feed(f[7:])
    assert out == [(4, msg.encode()), (33, b"\x02")]


def test_decoder_skips_garbage_and_bad_crc():
    good = encode("{}", ProtocolType.JSON)
    bad = bytearray(good); bad[-1] ^= 1
    assert FrameDecoder().feed(b"junk" + bytes(bad) + good) == [(4, b"{}")]


def test_discovery_crypto_roundtrip():
    blob = encrypt(b'{"a":1}', PRIMARY_KEY)
    assert len(blob) == 32 and decrypt(blob, PRIMARY_KEY) == b'{"a":1}'
    assert decrypt(encrypt(b"x" * 40, COMMON_KEY), COMMON_KEY) == b"x" * 40
