"""Simple geometry helpers (mm, canvas coordinates: +Y is down)."""
import math


def arc(cx, cy, r, a0, a1, step=0.2, ry=None):
    """Points along an arc from angle a0 to a1 (degrees), ~step mm apart."""
    ry = r if ry is None else ry
    span = math.radians(a1 - a0)
    n = max(8, math.ceil(abs(span) * max(r, ry) / step))
    return [(cx + r * math.cos(math.radians(a0) + span * i / n),
             cy + ry * math.sin(math.radians(a0) + span * i / n)) for i in range(n + 1)]


def circle(cx, cy, r, ry=None, start=-90):
    return arc(cx, cy, r, start, start + 360, ry=ry)


def smiley(cx=110.0, cy=110.0, d=40.0):
    """Closed paths for a cut-out smiley: [left eye, right eye, mouth, face].

    Inner features come first so they are cut before the outline frees the piece.
    """
    r = d / 2
    eye_rx, eye_ry = r * 0.12, r * 0.2
    eyes = [circle(cx + dx, cy - r * 0.3, eye_rx, eye_ry) for dx in (-r * 0.35, r * 0.35)]
    # Mouth: crescent between a lower arc and a flatter upper arc sharing endpoints.
    mr, a = r * 0.6, 20.0
    mcy = cy + r * 0.1
    ex, ey = mr * math.cos(math.radians(a)), mcy + mr * math.sin(math.radians(a))
    ucy = mcy - r * 0.25
    ur = math.hypot(ex, ey - ucy)
    ua = math.degrees(math.atan2(ey - ucy, ex))
    lower = arc(cx, mcy, mr, a, 180 - a)
    upper = arc(cx, ucy, ur, 180 - ua, ua)
    mouth = lower + upper[1:]
    mouth[-1] = mouth[0]
    face = circle(cx, cy, r)
    return eyes + [mouth, face]
