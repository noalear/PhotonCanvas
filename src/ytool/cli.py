"""ytool: read-only probe for xTool F-series lasers (F2 Ultra first).

    python -m ytool discover
    python -m ytool info <ip>
    python -m ytool watch <ip>
    python -m ytool snap <ip> [-o out.jpg] [--stream 0|1]
    python -m ytool view <ip> [--stream 0|1]     live camera window
    python -m ytool get <ip> /v1/some/url

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

    args = p.parse_args(argv)
    if args.cmd == "discover":
        cmd_discover(args)
        return
    if args.cmd == "view":
        from .viewer import Viewer
        Viewer(args.ip, args.stream).run()
        return
    try:
        asyncio.run({"info": cmd_info, "watch": cmd_watch, "snap": cmd_snap, "get": cmd_get}[args.cmd](args))
    except KeyboardInterrupt:
        pass
