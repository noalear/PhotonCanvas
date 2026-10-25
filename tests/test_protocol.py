import json
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "src"))

from pcprobe.discovery import decrypt, encrypt

# Stand-in keys: the tests never contain or need the real ones.
REQUEST_KEY = b"test-request-key-0123456789abcde"
REPLY_KEY = b"test-reply-key-0123456789abcdefg"
from pcprobe.frame import FrameDecoder, ProtocolType, crc16, encode


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
    blob = encrypt(b'{"a":1}', REQUEST_KEY)
    assert len(blob) == 32 and decrypt(blob, REQUEST_KEY) == b'{"a":1}'
    assert decrypt(encrypt(b"x" * 40, REPLY_KEY), REPLY_KEY) == b"x" * 40


def test_bgcode_decode():
    from pcprobe.bgcode import decode, tidy_numbers
    raw = (b"\xc3 GS004-4 HEAD\n"
           b"\xe7\x00\xf8\x01\x43\xce\x67\x40\xf9\x01\x04\xce\x87\x40\n"   # G0X0143.6740Y0104.8740
           b"\xe7\x01\xf3\x07\x00\xe6\x24\x00\n"                              # G1S0700F2400
           b"\xed\x05\x23\xf0\x40\n"                                          # M523P40
           b"\xe7\x04\xf0\x05\n")                                             # G4P5
    assert tidy_numbers(decode(raw)).splitlines() == [
        "# GS004-4 HEAD", "G0X143.674Y104.874", "G1S700F2400", "M523P40", "G4P5"]
