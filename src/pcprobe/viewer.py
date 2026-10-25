"""Live camera window: polls snapshots continuously (~4 fps on the overview camera).

The F2 Ultra firmware accepts /v1/platform/camera/live but never sends media frames,
so continuous snapshots are the live view.
"""
import asyncio
import io
import threading
import time
import tkinter as tk

from PIL import Image, ImageTk

from .device import Device


class Viewer:
    def __init__(self, ip, stream=0):
        self.ip = ip
        self.stream = stream
        self.latest = None
        self.fps = 0.0
        self.error = None
        self.running = True

    def _worker(self):
        async def loop():
            dev = Device(self.ip)
            await dev.connect()
            try:
                times = []
                while self.running:
                    data = await dev.snapshot(self.stream)
                    self.latest = data
                    times = [t for t in times if time.time() - t < 3] + [time.time()]
                    self.fps = len(times) / 3
            finally:
                await dev.close()
        try:
            asyncio.run(loop())
        except Exception as e:
            self.error = repr(e)

    def run(self):
        threading.Thread(target=self._worker, daemon=True).start()
        root = tk.Tk()
        root.title(f"pcprobe live: {self.ip} ({'close-up' if self.stream else 'overview'})")
        root.geometry("1000x780")
        label = tk.Label(root, bg="black")
        label.pack(fill="both", expand=True)
        status = tk.Label(root, anchor="w")
        status.pack(fill="x")
        shown = {"data": None, "img": None}

        def tick():
            if self.error:
                status.config(text=f"error: {self.error}")
            elif self.latest is not None and self.latest is not shown["data"]:
                shown["data"] = self.latest
                img = Image.open(io.BytesIO(self.latest))
                w, h = max(label.winfo_width(), 100), max(label.winfo_height(), 100)
                img.draft("RGB", (w, h))  # fast JPEG downscale on decode
                img.thumbnail((w, h))
                shown["img"] = ImageTk.PhotoImage(img)
                label.config(image=shown["img"])
                status.config(text=f"{self.fps:.1f} fps")
            root.after(30, tick)

        def on_close():
            self.running = False
            root.destroy()

        root.protocol("WM_DELETE_WINDOW", on_close)
        tick()
        root.mainloop()
