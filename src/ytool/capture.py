"""Save every job the stock app sends to the machine.

The device keeps the last uploaded job as `tmp.xf` (a tar). We poll its MD5 via
the download request (no data transferred) and fetch it when it changes and has
settled, then unpack it and decode motion.bgcode to text.
"""
import asyncio
import io
import json
import tarfile
import time
from pathlib import Path

from .bgcode import decode, tidy_numbers
from .device import Device

CHANNEL = 5  # stay off channel 1, which the stock app uses


async def job_digest(dev):
    resp = await dev.request("PUT", "/v1/filetransfer/download", raise_on_error=False, data={
        "filetype": 1, "filename": "tmp.xf", "digesttype": 1, "channel": CHANNEL, "packetsize": 1048576})
    if resp.get("code") != 0:
        return None
    await dev.request("PUT", "/v1/filetransfer/finish", raise_on_error=False,
                      data={"code": -9, "message": "probe only", "channel": CHANNEL})
    return resp["data"].get("digestdata")


def summarize(desc):
    parts = [desc.get("light", "")]
    for p in desc.get("processingParams", []):
        parts.append(f"{p.get('processingType', '?')}-{p.get('power')}p-{p.get('speed')}mms-x{p.get('repeat')}")
    return "_".join(x for x in parts if x)[:120]


def save_job(data, out_dir: Path):
    tar = tarfile.open(fileobj=io.BytesIO(data))
    members = {m.name: tar.extractfile(m).read() for m in tar.getmembers() if m.isfile()}
    desc = json.loads(members.get("description.json", b"{}") or b"{}")
    name = time.strftime("%Y%m%d-%H%M%S_") + summarize(desc)
    d = out_dir / name
    d.mkdir(parents=True, exist_ok=True)
    (d / "job.xf").write_bytes(data)
    for fname, content in members.items():
        (d / Path(fname).name).write_bytes(content)
    if "motion.bgcode" in members:
        (d / "motion.gcode").write_text(tidy_numbers(decode(members["motion.bgcode"])), encoding="utf-8")
    return d


async def capture(ip, out_dir, interval=3.0, save_existing=False):
    out_dir = Path(out_dir)
    dev = Device(ip)
    await dev.connect()
    try:
        last = await job_digest(dev)
        if save_existing and last:
            print("saved existing job:", save_job(await dev.download("tmp.xf", 1, channel=CHANNEL), out_dir))
        print("Watching for new jobs. Send jobs from xTool Studio; Ctrl+C to stop.")
        pending, pending_since = None, 0
        while True:
            await asyncio.sleep(interval)
            cur = await job_digest(dev)
            if cur and cur != last:
                if cur != pending:
                    pending, pending_since = cur, time.time()  # wait for it to settle
                    continue
                data = await dev.download("tmp.xf", 1, channel=CHANNEL)
                print("captured:", save_job(data, out_dir))
                last, pending = cur, None
    finally:
        await dev.close()
