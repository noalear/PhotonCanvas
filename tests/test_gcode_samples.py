"""Compare our generator with real xTool Studio exports in captures/gc (not in git; skipped if absent)."""
import os
import re
import sys
from pathlib import Path

import pytest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "src"))
from ytool.gcode import AIR_ENGRAVE, BLUE, RED, DotBitmap, Fill, Job, Laser, VectorPath

GC = Path(__file__).parent.parent / "captures" / "gc"


def norm(text):
    return [l for l in text.splitlines() if l.strip() and not l.startswith("# date=")]


def sample(name):
    p = GC / name
    if not p.exists():
        pytest.skip(f"missing capture {name}")
    return p.read_text()


def diff(ours, theirs):
    a, b = norm(ours), norm(theirs)
    for i, (x, y) in enumerate(zip(a, b)):
        if x != y:
            return f"line {i}: ours={x!r} theirs={y!r}"
    return None if len(a) == len(b) else f"length ours={len(a)} theirs={len(b)}"


SQUARE = [(100, 100), (120, 100), (120, 120), (100, 120), (100, 100)]


def test_blue_cut_1pass():
    job = Job([VectorPath(SQUARE, Laser(BLUE, 70, 40, 1))])
    assert diff(job.render(), sample("blue_cut_70p_40mms_1pass.gc")) is None


def test_red_cut_8pass():
    job = Job([VectorPath(SQUARE, Laser(RED, 70, 40, 8, pulse_width=200, frequency=65))])
    assert diff(job.render(), sample("red_cut_70p_40mms_8pass_65khz_200ns.gc")) is None


def test_blue_fill():
    poly = [(100, 100), (120, 100), (120, 120), (100, 120)]
    job = Job([Fill([poly], Laser(BLUE, 90, 1250, 1), lines_per_cm=100)], air=AIR_ENGRAVE)
    assert diff(job.render(), sample("blue_fill_90p_1250mms_100lpcm.gc")) is None


def test_dot_bitmap_emission():
    theirs = sample("blue_image_90p_200us_180dpi_jarvis.gc")
    pitch = 25.4 / 180
    dots, x, y = [], None, None
    for l in theirs.splitlines():
        if not l.startswith("G25X") and not l.startswith("G25Y"):
            continue
        m = re.findall(r"([XY])(-?[\d.]+)", l)
        for k, v in m:
            if k == "X": x = float(v)
            else: y = float(v)
        dots.append((x, y))
    ox, oy = min(d[0] for d in dots), min(d[1] for d in dots)
    cells = {(round((y - oy) / pitch), round((x - ox) / pitch)) for x, y in dots}
    rows = max(r for r, _ in cells) + 1
    cols = max(c for _, c in cells) + 1
    pixels = [[(r, c) in cells for c in range(cols)] for r in range(rows)]
    job = Job([DotBitmap(pixels, (ox, oy), Laser(BLUE, 90, 80, 1), dpi=180, dot_us=200, min_power=1)],
              air=AIR_ENGRAVE)
    assert diff(job.render(), theirs) is None
