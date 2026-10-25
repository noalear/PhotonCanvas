"""Package a job as the `.xf` tar the F2 Ultra expects (ustar, mode 0666)."""
import io
import json
import tarfile
import time

from PIL import Image, ImageDraw


def preview_jpg(paths, size=200):
    xs = [x for p in paths for x, _ in p]
    ys = [y for p in paths for _, y in p]
    x0, y0 = min(xs), min(ys)
    scale = (size - 20) / max(max(xs) - x0, max(ys) - y0, 1e-6)
    img = Image.new("RGB", (size, size), "white")
    d = ImageDraw.Draw(img)
    for p in paths:
        d.line([(10 + (x - x0) * scale, 10 + (y - y0) * scale) for x, y in p], fill="black", width=2)
    buf = io.BytesIO()
    img.save(buf, "JPEG", quality=85)
    return buf.getvalue()


def build_xf(motion: str, border: str, preview: bytes, light: str, params: list) -> bytes:
    desc = {"mode": "LASER_PLANE", "light": light,
            "create_time": time.strftime("%Y/%m/%d %H:%M:%S"), "axis": "",
            "processingParams": params, "material": 1}
    members = [("preview.jpg", preview), ("border.gcode", border.encode()),
               ("motion.gcode", motion.encode()),
               ("description.json", json.dumps(desc, separators=(",", ":")).encode())]
    buf = io.BytesIO()
    with tarfile.open(fileobj=buf, mode="w", format=tarfile.USTAR_FORMAT) as tar:
        for name, data in members:
            info = tarfile.TarInfo(name)
            info.size, info.mode, info.mtime = len(data), 0o666, int(time.time())
            tar.addfile(info, io.BytesIO(data))
    return buf.getvalue()
