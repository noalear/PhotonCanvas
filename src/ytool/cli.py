"""ytool: read-only probe for xTool F-series lasers (F2 Ultra first).

    python -m ytool discover
    python -m ytool info <ip>
    python -m ytool watch <ip>
    python -m ytool snap <ip> [-o out.jpg] [--stream 0|1]
    python -m ytool view <ip> [--stream 0|1]     live camera window
    python -m ytool get <ip> /v1/some/url
    python -m ytool capture <ip> [-o captures/jobs]     save every job sent by the stock app

Nothing here moves the head or fires the laser.
"""
import argparse
import asyncio
import json
import sys
import time
import urllib.request
from pathlib import Path

from .device import Device, DeviceError
from .discovery import discover


def _dump(obj):
    print(json.dumps(obj, indent=2, ensure_ascii=False))


def _button_prompt():
    print(">>> Press the button on the machine to authorize this computer...", flush=True)


async def _connected(args):
    dev = Device(args.ip, trace=args.trace)
    try:
        await dev.connect(on_button=_button_prompt)
    except DeviceError as e:
        await dev.close()
        sys.exit(f"pairing failed: {e}")
    except (OSError, asyncio.TimeoutError) as e:
        await dev.close()
        sys.exit(f"could not open wss://{args.ip}:28900 ({e!r}). "
                 f"The machine may use the older V1 protocol; try: python -m ytool info {args.ip} --v1")
    return dev


def _v1_get(ip, path, port=8080, timeout=5):
    with urllib.request.urlopen(f"http://{ip}:{port}{path}", timeout=timeout) as r:
        body = r.read()
    try:
        return json.loads(body)
    except ValueError:
        return body


def cmd_discover(args):
    found = discover(timeout=args.timeout, target_ip=args.ip, verbose=args.trace)
    if not found:
        print("No devices answered. Check the machine is on and on the same network, "
              "and that Windows Firewall allows Python (UDP). Try --trace.")
        return
    for ip, info in found.items():
        print(f"{ip}  [{info.get('protocol')}]")
        _dump(info)


async def cmd_info(args):
    if args.v1:
        for path in ("/device/machineInfo", "/device/runningStatus", "/system?action=version_v2"):
            try:
                print(f"--- {path}")
                _dump(_v1_get(args.ip, path))
            except Exception as e:
                print(f"  failed: {e}")
        return
    dev = await _connected(args)
    try:
        for url in ("/v1/device/machineInfo", "/v1/device/runtime-infos", "/v1/processing/progress"):
            print(f"--- {url}")
            try:
                _dump(await dev.get(url))
            except DeviceError as e:
                print(f"  {e}")
    finally:
        await dev.close()


async def cmd_watch(args):
    dev = await _connected(args)
    print("Connected. Printing device events (Ctrl+C to stop)...")
    last_poll = 0
    try:
        while True:
            try:
                ev = await asyncio.wait_for(dev.events.get(), 1)
                print(time.strftime("%H:%M:%S"), json.dumps(ev, ensure_ascii=False))
            except asyncio.TimeoutError:
                pass
            if args.poll and time.time() - last_poll > args.poll:
                last_poll = time.time()
                try:
                    print(time.strftime("%H:%M:%S"), "progress", json.dumps(await dev.get("/v1/processing/progress")))
                except DeviceError as e:
                    print("  progress:", e)
    finally:
        await dev.close()


async def cmd_snap(args):
    out = Path(args.output or f"snap_{args.ip}_{args.stream}_{int(time.time())}.jpg")
    if args.v1:
        name = "deep" if args.stream else "main"
        data = _v1_get(args.ip, f"/snap?name={name}&stream={args.stream}", port=8329, timeout=60)
        if not isinstance(data, bytes):
            sys.exit(f"camera error: {data}")
    else:
        dev = await _connected(args)
        try:
            t0 = time.time()
            data = await dev.snapshot(args.stream)
            print(f"snapshot took {time.time() - t0:.2f}s")
        finally:
            await dev.close()
    out.write_bytes(data)
    print(f"saved {out} ({len(data)} bytes)")


async def cmd_capture(args):
    from .capture import capture
    await capture(args.ip, args.out, save_existing=args.save_existing)


def build_smiley(args):
    from .gcode import BLUE, RED, Job, Laser, VectorPath
    from .shapes import smiley
    paths = smiley(args.x, args.y, args.size)
    laser = Laser(args.light, args.power, args.speed, args.passes)
    job = Job([VectorPath(p, laser) for p in paths], passes=args.passes, pass_delay=args.pass_delay)
    return paths, job


async def cmd_smiley(args):
    """Cut a smiley. Dry run unless --send; with --send the machine waits for its button."""
    import uuid
    from .xf import build_xf, preview_jpg
    paths, job = build_smiley(args)
    motion = job.render()
    border = job.border(paths[-1])
    params = [{"processingType": "VECTOR_CUTTING", "processingLightSource": args.light, "power": args.power,
               "speed": args.speed, "repeat": args.passes, "isFill": False}]
    xf = build_xf(motion, border, preview_jpg(paths), args.light, params)
    out = Path(args.out)
    out.mkdir(parents=True, exist_ok=True)
    (out / "motion.gcode").write_text(motion, encoding="utf-8")
    (out / "border.gcode").write_text(border, encoding="utf-8")
    (out / "job.xf").write_bytes(xf)
    from PIL import Image, ImageDraw
    img = Image.new("RGB", (800, 800), "white")
    d = ImageDraw.Draw(img)
    s_ = 800 / 220
    d.rectangle([0, 0, 799, 799], outline="#999")
    for i, p in enumerate(paths):
        d.line([(x * s_, y * s_) for x, y in p], fill="red" if i < len(paths) - 1 else "black", width=2)
    img.save(out / "preview.png")
    n_g1 = motion.count("G1X")
    print(f"wrote {out}/motion.gcode ({n_g1} moves, {len(paths)} paths), border.gcode, job.xf ({len(xf)} B), preview.png")
    print(f"settings: {args.light} {args.power}% {args.speed} mm/s, {args.passes} pass(es)"
          + (f", {args.pass_delay}s pause between whole-shape passes" if args.passes > 1 else "")
          + f", {args.size} mm at ({args.x}, {args.y}), distance {args.distance} mm")
    if not args.send:
        print("dry run only (add --send to upload; the machine will then wait for its button)")
        return

    dev = await _connected(args)
    try:
        z = (await dev.request("GET", "/v1/laser-head/focus/parameter", data={"action": "coord"}))["value"]
        want = args.distance + 6
        if abs(z - want) > 0.05:
            sys.exit(f"laser head is at z={z} (distance {z - 6:.2f} mm), expected {want} for {args.distance} mm. "
                     f"Set the distance in xTool Studio or on the machine first.")
        mode = (await dev.get("/v1/device/runtime-infos"))["curMode"]["mode"]
        if mode != "P_IDLE":
            sys.exit(f"machine is busy ({mode}); not uploading")
        print(f"z={z} OK (distance {args.distance} mm), machine idle. Uploading...")
        await dev.upload(xf, "tmp.xf", 1, progress=lambda a, b: print(f"  sent {a}/{b}"))
        task = str(uuid.uuid4())
        await dev.request("PUT", "/v1/processing/upload/config",
                          data={"fileType": "xf", "autoStart": 0, "taskId": task})
        print("Uploaded. >>> Press the button on the machine to start (or cancel on the machine). <<<")
        print("Watching machine events (Ctrl+C to stop watching; this does not stop the job):")
        while True:
            ev = await dev.events.get()
            print(time.strftime("%H:%M:%S"), json.dumps(ev, ensure_ascii=False))
            if isinstance(ev, dict) and str(ev.get("info", "")).upper() in ("WORK_FINISHED", "WORK_STOPED"):
                break
    finally:
        await dev.close()


async def cmd_get(args):
    if not args.url.startswith("/v1/"):
        sys.exit("only GET /v1/... URLs are allowed here")
    dev = await _connected(args)
    try:
        resp = await dev.request("GET", args.url, raise_on_error=False)
        _dump(resp)
    finally:
        await dev.close()


def main(argv=None):
    p = argparse.ArgumentParser(prog="ytool", description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--trace", action="store_true", help="print every frame sent/received")
    sub = p.add_subparsers(dest="cmd", required=True)

    s = sub.add_parser("discover"); s.add_argument("--ip", help="probe one IP directly"); s.add_argument("--timeout", type=float, default=5)
    s = sub.add_parser("info"); s.add_argument("ip"); s.add_argument("--v1", action="store_true")
    s = sub.add_parser("watch"); s.add_argument("ip"); s.add_argument("--poll", type=float, default=0, help="also poll progress every N s")
    s = sub.add_parser("snap"); s.add_argument("ip"); s.add_argument("-o", "--output"); s.add_argument("--stream", type=int, default=0, choices=(0, 1)); s.add_argument("--v1", action="store_true")
    s = sub.add_parser("view"); s.add_argument("ip"); s.add_argument("--stream", type=int, default=0, choices=(0, 1))
    s = sub.add_parser("get"); s.add_argument("ip"); s.add_argument("url")
    s = sub.add_parser("smiley", help="cut a smiley face (dry run unless --send)")
    s.add_argument("ip"); s.add_argument("--power", type=float, required=True); s.add_argument("--speed", type=float, required=True)
    s.add_argument("--light", choices=("blue", "red"), default="blue"); s.add_argument("--passes", type=int, default=1)
    s.add_argument("--pass-delay", type=float, default=0, help="seconds to pause between whole-shape passes")
    s.add_argument("--distance", type=float, required=True, help="material distance in mm (head z = distance + 6)")
    s.add_argument("--size", type=float, default=40); s.add_argument("--x", type=float, default=110); s.add_argument("--y", type=float, default=110)
    s.add_argument("-o", "--out", default="captures/out/smiley"); s.add_argument("--send", action="store_true")
    s = sub.add_parser("capture"); s.add_argument("ip"); s.add_argument("-o", "--out", default="captures/jobs"); s.add_argument("--save-existing", action="store_true")

    args = p.parse_args(argv)
    if args.cmd == "discover":
        cmd_discover(args)
        return
    if args.cmd == "view":
        from .viewer import Viewer
        Viewer(args.ip, args.stream).run()
        return
    try:
        asyncio.run({"info": cmd_info, "watch": cmd_watch, "snap": cmd_snap, "get": cmd_get, "capture": cmd_capture, "smiley": cmd_smiley}[args.cmd](args))
    except KeyboardInterrupt:
        pass
