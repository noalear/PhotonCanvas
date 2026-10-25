"""G-code generator for the xTool F2 Ultra (GS004-CLASS-4).

Reproduces the dialect documented in docs/gcode-f2-ultra.md. Units: mm, mm/s,
power in percent. Nothing here talks to the machine.
"""
import json
import math
import time
from dataclasses import dataclass, field

BLUE, RED = "blue", "red"
AIR_CUT, AIR_ENGRAVE = 11, 1  # last preamble line: M9043 H11.. for cuts, H1.. for engraves (?)


def _num(v):
    """Vector-style number: up to 3 decimals, trailing zeros trimmed (100, 154.101)."""
    s = f"{v:.3f}".rstrip("0").rstrip(".")
    return "0" if s in ("-0", "") else s


def _f3(v):
    return f"{v:.3f}"


def _cfg(d):
    """JSON like JavaScript writes it: whole floats without '.0'."""
    clean = {k: (int(v) if isinstance(v, float) and v.is_integer() else v) for k, v in d.items()}
    return json.dumps(clean, separators=(",", ":"))


def _power(p):
    return int(round(p * 10))


def _speed(mm_s):
    return int(round(mm_s * 60))


@dataclass
class Laser:
    light: str = BLUE
    power: float = 50.0          # percent
    speed: float = 100.0         # mm/s
    passes: int = 1
    pulse_width: int = 200       # ns, red only
    frequency: int = 65          # kHz, red only

    def source_lines(self, vector=True):
        # Vector blocks set G90 after the source; bitmap/fill blocks don't.
        lines = ["G4P1", "G21" if self.light == BLUE else "G22"] + (["G90"] if vector else [])
        if self.light == RED:  # verified for vector only; assumed the same for bitmap
            lines += [f"G0H{self.pulse_width}", f"G0Q{self.frequency}"]
        return lines + ["", "G4P1"]


@dataclass
class Job:
    """A complete motion file.

    passes=None reproduces xTool's behaviour: each block loops its own
    laser.passes before the next block starts. passes=N runs the WHOLE job
    once per pass (every shape gets pass 1, then every shape gets pass 2, ...),
    with pass_delay seconds of dwell between passes.
    """
    blocks: list = field(default_factory=list)
    air: int = AIR_CUT
    created: float = None
    passes: int = None
    pass_delay: float = 0.0

    def header(self):
        t = time.localtime(self.created or time.time())
        return [
            "# date=" + time.strftime("%Y_%m_%d_%H_%M_%S", t),
            "# version=1.9.11",
            "# algorithmVersion=",
            '# gc={"size":{"w":220,"h":220}}',
            '# gc={"offset":{"x":0,"y":0}}',
            '# gc={"start":{"x":0,"y":0.0000}}',
            '# gc={"keys":["x","y"],"rm":1,"is3DMode":false}',
            "# timeConfig=",
            "G90",
            "G0 F3000",
            "G4P5",
            'G198 P84 "M9033 V100"',
            'G198 P85 "M9401 D2 V500"',
            "M9064 B3",
            'G198 P70 "M9064 B3"',
            'G198 P78 "M9064 B3"',
            "M9039 C2",
            'G198 P69 "M9039 C2"',
            'G198 P76 "M9039 V50"',
            'G198 P76 "M9043 {}"'.format(" ".join(f"{c}{self.air}" for c in "HIJKLM")),
            "G4P5",
            "# GS004-4 HEAD",
            "G0 F180000",
            "M4 S0",
            "G1 F180000",
            "G0 X0 Y0",
            "",
        ]

    def tail(self):
        return ["# END", "# GS004-4 TAIL", "G90", "G0 S0", "G1 F180000", "M536 U0", "M6 P1", ""]

    def render(self):
        lines = self.header()
        if self.passes is None:
            for b in self.blocks:
                lines += b.render() + [""]
        else:
            for p in range(self.passes):
                if p and self.pass_delay > 0:
                    lines += [f"# pass {p + 1} of {self.passes}", f"G4P{int(round(self.pass_delay * 1000))}", ""]
                for b in self.blocks:
                    lines += b.render(passes=1) + [""]
        return "\n".join(lines + self.tail())

    def border(self, outline, power_s=64, speed_f=960000):
        """Framing file traced by the machine's frame button (low power, very fast)."""
        lines = ["# GS004-4 HEAD", "G0 F180000", "M4 S0", "G1 F180000", "M114 S1", "", "",
                 "# GS004-4 VECTOR HEAD", "# motion_start", "", "G21", "G90", "", "", "", "",
                 '# blockConfig={"powerFactor":0.064,"isVector":true,"crossDot":false}',
                 f"G0X{_num(outline[0][0])}Y{_num(outline[0][1])}"]
        for i, (x, y) in enumerate(outline[1:]):
            lines.append(f"G1X{_num(x)}Y{_num(y)}" + (f"S{power_s}F{speed_f}" if i == 0 else ""))
        return "\n".join(lines + ["", "# GS004-4 VECTOR TAIL", "# motion_end"] + self.tail())


def _loop(passes, body):
    if passes <= 1:
        return body
    return ["# MUST", f"G104 P{passes}", ""] + body + ["", "# MUST", "G105"]


@dataclass
class VectorPath:
    """One polyline/polygon cut or line-engraved along its outline."""
    points: list                 # [(x, y), ...]; repeat the first point to close
    laser: Laser

    def render(self, passes=None):
        L = self.laser
        passes = L.passes if passes is None else passes
        cfg = _cfg({"powerFactor": round(L.power / 100, 6), "isVector": True, "crossDot": False})
        body = [f"# blockConfig={cfg}", f"G0X{_num(self.points[0][0])}Y{_num(self.points[0][1])}"]
        for i, (x, y) in enumerate(self.points[1:]):
            line = f"G1X{_num(x)}Y{_num(y)}"
            if i == 0:
                line += f"S{_power(L.power)}F{_speed(L.speed)}"
            body.append(line)
        return (["# GS004-4 VECTOR HEAD", "# motion_start"] + L.source_lines() + ["M523P40", "", ""]
                + _loop(passes, body) + ["", "# GS004-4 VECTOR TAIL", "# motion_end"])


def _scan_spans(polygons, y):
    """X intervals inside the polygons (even-odd rule) on scanline y."""
    xs = []
    for poly in polygons:
        n = len(poly)
        for i in range(n):
            (x1, y1), (x2, y2) = poly[i], poly[(i + 1) % n]
            if (y1 <= y < y2) or (y2 <= y < y1):
                xs.append(x1 + (y - y1) * (x2 - x1) / (y2 - y1))
    xs.sort()
    return [(xs[i], xs[i + 1]) for i in range(0, len(xs) - 1, 2)]


@dataclass
class Fill:
    """Zigzag line fill of closed polygons (even-odd)."""
    polygons: list               # [[(x, y), ...], ...] without repeated closing point
    laser: Laser
    lines_per_cm: float = 100.0
    overscan: float = 0.0
    accel: int = 5000

    def render(self, passes=None):
        L = self.laser
        passes = L.passes if passes is None else passes
        step = 10.0 / self.lines_per_cm
        ys = [p[1] for poly in self.polygons for p in poly]
        y0, y1 = min(ys), max(ys)
        cfg = {"powerFactor": round(L.power / 100, 6), "overDist": self.overscan, "accel": self.accel,
               "density": round(self.lines_per_cm / 10, 4), "power": [0, _power(L.power)],
               "bitmapMode": "grayscale", "crossDot": False, "startX": 0, "isVector": False}
        body = [f"# blockConfig={_cfg(cfg)}", "", "", f"G1F{_speed(L.speed)}S0"]
        s = _power(L.power)
        n_lines = int(math.floor((y1 - y0) / step + 1e-9))
        first, forward = True, True
        for i in range(n_lines):
            y = y0 + i * step
            spans = _scan_spans(self.polygons, y + 1e-9)
            if not spans:
                continue
            if not forward:
                spans = [(b, a) for a, b in reversed(spans)]
            for a, b in spans:
                if first:
                    body.append(f"G0X{_f3(a)}Y{_f3(y)}")
                    first = False
                elif body[-1].endswith(f"S{s}"):
                    body.append(f"G1Y{_f3(y)}S0" if _f3(a) == last_x else f"G1X{_f3(a)}Y{_f3(y)}S0")
                body.append(f"G1X{_f3(b)}S{s}")
                last_x = _f3(b)
            forward = not forward
        return (["# GS004-4 BITMAP HEAD", "# motion_start", "", "", ""] + L.source_lines(vector=False) + ["M523P40", "", ""]
                + _loop(passes, body) + ["", "# GS004-4 BITMAP TAIL", "# motion_end", ""])


@dataclass
class DotBitmap:
    """Dot-mode engraving of a 1-bit image (rows top-to-bottom in +Y, True = burn)."""
    pixels: list                 # list of rows, each a list/str of bools
    origin: tuple                # (x, y) of pixel (0, 0) centre
    laser: Laser
    dpi: float = 180.0
    dot_us: int = 200
    min_power: float = 1.0
    mode: str = "Jarvis"

    def render(self, passes=None):
        L = self.laser
        passes = L.passes if passes is None else passes
        pitch = 25.4 / self.dpi
        cfg = {"powerFactor": round(L.power / 100, 6), "overDist": 0, "accel": 5000,
               "density": round(self.dpi / 25.4, 4), "power": [_power(self.min_power), _power(L.power)],
               "bitmapMode": self.mode, "crossDot": False, "startX": 0, "isVector": False}
        body = [f"# blockConfig={_cfg(cfg)}", "", "",
                f"G1F{_speed(L.speed)}S0", f"G25T{round(self.dot_us / 10)}"]
        ox, oy = self.origin
        first, forward, last_x = True, True, None
        for r, row in enumerate(self.pixels):
            cols = [c for c, v in enumerate(row) if v]
            if not cols:
                continue  # empty rows keep the zigzag direction (?)
            if not forward:
                cols.reverse()
            y = _f3(oy + r * pitch)
            for k, c in enumerate(cols):
                x = _f3(ox + c * pitch)
                if first:
                    body += [f"G0X{x}Y{y}", "", f"G25X{x}Y{y}S{_power(L.power)}"]
                    first = False
                elif k == 0:
                    body.append(f"G25Y{y}" if x == last_x else f"G25X{x}Y{y}")
                else:
                    body.append(f"G25X{x}")
                last_x = x
            forward = not forward
        return (["# GS004-4 BITMAP HEAD", "# motion_start", "", "", ""] + L.source_lines(vector=False) + ["M523P10", "", ""]
                + _loop(passes, body) + ["", "# GS004-4 BITMAP TAIL", "# motion_end", ""])
