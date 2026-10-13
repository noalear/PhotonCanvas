"""Decode xTool's binary G-code (motion.bgcode) into text.

Format, worked out from real files (see docs/protocol-f2-ultra.md):
- lines end with 0x0A
- 0xC3 starts a comment; the rest of the line is ASCII ("# ...")
- 0xE1..0xFA are the letters A..Z
- 0xC0 is a space, 0xCE a decimal point, 0xCD a minus sign
- other bytes are packed BCD digit pairs (0x43 -> "43")

Unknown bytes are printed as <xx> so new tokens stand out.

Usage: python -m ytool.bgcode motion.bgcode [out.gcode]
"""
import sys

SPECIAL = {0xC0: " ", 0xCE: ".", 0xCD: "-", 0xC2: ""}


def decode(data: bytes) -> str:
    out = []
    i, n = 0, len(data)
    while i < n:
        b = data[i]
        if b == 0x0A:
            out.append("\n")
        elif b == 0xC3:
            j = data.find(b"\n", i)
            j = n if j < 0 else j
            out.append("#" + data[i + 1:j].decode("utf-8", "replace"))
            i = j
            continue
        elif 0xE1 <= b <= 0xFA:
            out.append(chr(ord("A") + b - 0xE1))
        elif b in SPECIAL:
            out.append(SPECIAL[b])
        elif (b >> 4) <= 9 and (b & 0xF) <= 9:
            out.append(f"{b >> 4}{b & 0xF}")
        else:
            out.append(f"<{b:02x}>")
        i += 1
    return "".join(out)


def tidy_numbers(text: str) -> str:
    """Strip BCD padding: X0143.6740 -> X143.674, G00 -> G0."""
    import re

    def fix(m):
        letter, num = m.group(1), m.group(2)
        neg = num.startswith("-")
        num = num.lstrip("-")
        if "." in num:
            whole, frac = num.split(".", 1)
            whole = whole.lstrip("0") or "0"
            frac = frac.rstrip("0")
            num = whole + ("." + frac if frac else "")
        else:
            num = num.lstrip("0") or "0"
        return letter + ("-" if neg else "") + num

    lines = []
    for line in text.split("\n"):
        if line.startswith("#"):
            lines.append(line)
        else:
            lines.append(re.sub(r"([A-Z])(-?[0-9.]+)", fix, line))
    return "\n".join(lines)


if __name__ == "__main__":
    raw = open(sys.argv[1], "rb").read()
    text = tidy_numbers(decode(raw))
    if len(sys.argv) > 2:
        open(sys.argv[2], "w", encoding="utf-8").write(text)
    else:
        sys.stdout.write(text)
