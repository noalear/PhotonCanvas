"""A small window for testing whether the rotary/conveyor motor turns outside a job.

    python -m pcprobe.motor_test [ip]       (or double-click tools/motor-test.cmd)

Nothing moves until a button is pressed. It only ever sends PUT /v1/peripheral/param with
type=conveyor (the machine refuses type=rotary with "invalid type"), so the laser and the head
are not involved. A machine answer of "Success" only means the command was accepted (a turn sent
while the motor isn't held is accepted and ignored), so the window asks what the chuck actually did
and saves commands, answers and observations to a text file.

The machine calls u millimetres, but it knows nothing about the object, so here they are motor units.
Each goto turns the motor by u more; it is not a position.
"""
import argparse
import asyncio
import ctypes
import json
import queue
import threading
import time
import tkinter as tk
import traceback
from dataclasses import dataclass, field
from pathlib import Path
from tkinter import ttk

from .device import Device
from .discovery import discover

URL = "/v1/peripheral/param"
PORT_TYPE = "conveyor"
ACTIONS = ("lock", "goto", "stop", "free")
IDLE = "P_IDLE"
MIN_MM, MAX_MM, DEFAULT_MM = 0.5, 10.0, 5.0
MIN_SPEED, MAX_SPEED, DEFAULT_SPEED = 100, 1200, 1200
UNSURE = ("unsure", "Not sure")
# What a chuck's full turn is inside a job (M535U = 128 / circumference, docs/gcode-f2-ultra.md).
# Whether the same holds for these commands is what the count in the window is there to check.
UNITS_PER_CHUCK_TURN = 128


@dataclass(frozen=True)
class Step:
    key: str
    button: str
    does: str
    watch: str
    data: dict
    choices: tuple = ()
    needs_idle: bool = True


def check_settings(mm, speed):
    if not MIN_MM <= mm <= MAX_MM:
        raise ValueError(f"amount must be {MIN_MM:g} to {MAX_MM:g} units, not {mm:g}")
    if not MIN_SPEED <= speed <= MAX_SPEED:
        raise ValueError(f"speed must be {MIN_SPEED} to {MAX_SPEED}, not {speed:g}")


def parse_settings(mm_text, speed_text):
    """(mm, speed), or None when either box doesn't hold an allowed number."""
    try:
        mm, speed = float(mm_text), float(speed_text)
        check_settings(mm, speed)
    except ValueError:
        return None
    return mm, speed


def make_steps(mm, speed):
    check_settings(mm, speed)
    return (
        Step("hold", "Hold the motor", "Locks the motor. It only turns while it's held. Pressing this starts the count again.",
             "Listen for a faint hum and try the chuck by hand: it should resist.",
             {"action": "lock"}, (("held", "It held"), ("no_change", "No change"), UNSURE)),
        Step("turn", f"Turn {mm:g} units", f"Turns the motor {mm:g} units at speed {speed:g}. Each press turns it again.",
             "The chuck should turn a little.",
             {"u": mm, "action": "goto", "speed": speed}, (("turned", "It turned"), ("still", "It stayed still"), UNSURE)),
        Step("free", "Let the motor go", "Releases the motor. It stays released when you close this window.",
             "The chuck should now turn freely by hand.",
             {"action": "free"}, (("limp", "It went limp"), ("no_change", "No change"), UNSURE)),
    )


STOP = Step("stop", "Stop the motor", "Stops any turn in progress.", "", {"action": "stop"}, needs_idle=False)


def verdict(seen):
    """Plain sentences from what the person watching recorded, keyed by step."""
    lines = []
    if seen.get("turn") == "turned":
        lines.append("Yes: the motor turns outside a job.")
    elif seen.get("turn") == "still":
        lines.append("No turn seen so far, even though the machine accepted the command.")
    if seen.get("hold") == "held":
        lines.append("Hold works.")
    elif seen.get("hold") == "no_change":
        lines.append("Hold made no visible change.")
    if seen.get("free") == "limp":
        lines.append("Let go works.")
    elif seen.get("free") == "no_change":
        lines.append("Let go made no visible change.")
    return " ".join(lines) or "Nothing recorded yet."


def describe_mode(mode):
    return {IDLE: "ready", "P_SLEEP": "asleep"}.get(mode, f"busy ({mode})")


@dataclass
class Answer:
    accepted: bool
    text: str
    raw: str = field(default="")


async def machine_mode(dev):
    return (await dev.get("/v1/device/runtime-infos"))["curMode"]["mode"]


async def send_step(dev, step, timeout=10):
    """Send one step. Refuses anything but the four motor actions, and checks the machine is idle."""
    if step.data.get("action") not in ACTIONS:
        raise ValueError(f"not a motor action: {step.data!r}")
    if step.needs_idle:
        mode = await machine_mode(dev)
        if mode != IDLE:
            return Answer(False, f"Not sent: the machine is {describe_mode(mode)}.")
    try:
        resp = await dev.request("PUT", URL, data=step.data, params={"type": PORT_TYPE},
                                 timeout=timeout, raise_on_error=False)
    except asyncio.TimeoutError:
        return Answer(False, "No answer from the machine.")
    raw = json.dumps(resp, ensure_ascii=False)
    if resp.get("code") == 0:
        return Answer(True, "Machine accepted it. What did the chuck do?", raw)
    return Answer(False, f"Machine refused it: {resp.get('message')} (code {resp.get('code')}).", raw)


class ResultLog:
    def __init__(self, folder=None):
        folder = Path(folder) if folder else Path.home() / "PhotonCanvas" / "motor-test"
        folder.mkdir(parents=True, exist_ok=True)
        self.path = folder / time.strftime("motor-test-%Y%m%d-%H%M%S.txt")

    def write(self, text):
        with self.path.open("a", encoding="utf-8") as f:
            f.write(f"{time.strftime('%H:%M:%S')} {text}\n")


class Link:
    """Keeps a connection open on its own thread and reports through a queue the window reads."""

    def __init__(self, ip=None):
        self.ip = ip
        self.events = queue.Queue()
        self.dev = None
        self.loop = None
        self.running = True

    def start(self):
        threading.Thread(target=lambda: asyncio.run(self._main()), daemon=True).start()

    def stop(self):
        self.running = False

    def send(self, step):
        if self.loop is None:
            self.events.put(("answer", step, Answer(False, "Not connected to the machine.")))
            return
        asyncio.run_coroutine_threadsafe(self._send(step), self.loop)

    async def _send(self, step):
        try:
            if self.dev is None:
                answer = Answer(False, "Not connected to the machine.")
            else:
                answer = await send_step(self.dev, step)
        except Exception as e:
            answer = Answer(False, f"Could not send it: {e!r}")
        self.events.put(("answer", step, answer))

    async def _find_ip(self):
        if self.ip:
            return self.ip
        self.events.put(("status", "Looking for the machine on the network...", False))
        found = await asyncio.to_thread(discover, 4.0)
        return next(iter(found), None)

    async def _main(self):
        self.loop = asyncio.get_running_loop()
        while self.running:
            dev = None
            try:
                ip = await self._find_ip()
                if not ip:
                    self.events.put(("status", "Machine not found. Is it on? Still looking...", False))
                else:
                    self.events.put(("status", f"Connecting to {ip}...", False))
                    dev = Device(ip)
                    await dev.connect(on_button=lambda: self.events.put(
                        ("status", "Press the button on the machine to allow this computer.", False)))
                    self.dev = dev
                    while self.running:
                        self.events.put(("mode", ip, await machine_mode(dev)))
                        await asyncio.sleep(2)
            except Exception as e:
                self.events.put(("status", f"Lost the machine ({e!r}). Trying again...", False))
            finally:
                self.dev = None
                if dev:
                    try:
                        await dev.close()
                    except Exception:
                        pass
            if self.running:
                await asyncio.sleep(3)


class Window:
    GOOD, WARN, BAD = "#1a7f37", "#9a6700", "#cf222e"

    def __init__(self, root, link, log):
        self.root, self.link, self.log = root, link, log
        self.mode = None
        self.pending = set()
        self.seen = {}
        self.rows = {}
        self.choice_vars = {}
        self.held = False
        self.total = 0.0
        root.title("PhotonCanvas motor test")
        root.report_callback_exception = self._crashed
        root.protocol("WM_DELETE_WINDOW", self.close)
        self.mm = tk.StringVar(value=f"{DEFAULT_MM:g}")
        self.speed = tk.StringVar(value=str(DEFAULT_SPEED))
        self._build()
        self.mm.trace_add("write", lambda *_: self.refresh())
        self.speed.trace_add("write", lambda *_: self.refresh())
        self.log.write(f"window opened; commands go to {URL}?type={PORT_TYPE}")
        self.refresh()
        self._read_events()

    # ---- layout -----------------------------------------------------------
    def _build(self):
        top = ttk.Frame(self.root, padding=14)
        top.grid(sticky="nsew")
        top.columnconfigure(0, weight=1)
        ttk.Label(top, text="Test the rotary motor", font=("Segoe UI", 15, "bold")).grid(sticky="w")
        ttk.Label(top, wraplength=860, text=(
            "Press the steps in order and say what the chuck did after each one. The laser stays off and the "
            "head does not move: only the motor on the rotary port turns, and only when you press a button."
        )).grid(sticky="w", pady=(2, 8))
        self.status = tk.Label(top, anchor="w", font=("Segoe UI", 10, "bold"), text="Starting...")
        self.status.grid(sticky="w", pady=(0, 8))

        box = ttk.LabelFrame(top, text="How much to turn", padding=8)
        box.grid(sticky="ew", pady=(0, 8))
        box.columnconfigure(2, weight=1)
        ttk.Label(box, text=f"Amount, motor units ({MIN_MM:g} to {MAX_MM:g})").grid(row=0, column=0, sticky="w", padx=(0, 12))
        ttk.Label(box, text=f"Speed ({MIN_SPEED} to {MAX_SPEED})").grid(row=0, column=1, sticky="w", padx=(0, 12))
        ttk.Spinbox(box, from_=MIN_MM, to=MAX_MM, increment=0.5, width=8, textvariable=self.mm).grid(
            row=1, column=0, sticky="w")
        ttk.Spinbox(box, from_=MIN_SPEED, to=MAX_SPEED, increment=100, width=8, textvariable=self.speed).grid(
            row=1, column=1, sticky="w")
        self.ruler = tk.Canvas(box, width=420, height=64, highlightthickness=0)
        self.ruler.grid(row=0, column=2, rowspan=2, sticky="e")

        steps = ttk.LabelFrame(top, text="The test", padding=8)
        steps.grid(sticky="ew", pady=(0, 8))
        steps.columnconfigure(1, weight=1)
        for i, step in enumerate(make_steps(DEFAULT_MM, DEFAULT_SPEED)):
            self._build_row(steps, i, step)

        stop = tk.Button(top, text="Stop the motor", bg=self.BAD, fg="white", font=("Segoe UI", 11, "bold"),
                         activebackground="#a40e26", activeforeground="white", padx=18, pady=4,
                         command=lambda: self.press(STOP))
        stop.grid(sticky="w", pady=(0, 2))
        self.stop_button = stop
        self.stop_answer = ttk.Label(top, text="Use this at any time if the motor keeps turning.")
        self.stop_answer.grid(sticky="w", pady=(0, 8))

        measure = ttk.LabelFrame(top, text="How far is one full turn?", padding=8)
        measure.grid(sticky="ew", pady=(0, 8))
        ttk.Label(measure, wraplength=840, text=(
            f"No rod needed. Put a tape mark on the chuck, press Hold, set the amount to 8 and press Turn "
            f"{UNITS_PER_CHUCK_TURN // 8} times, about a second apart. If a full turn is {UNITS_PER_CHUCK_TURN} "
            f"units here, as it is inside a job, the mark ends up back where it started."
        )).grid(sticky="w")
        self.count = ttk.Label(measure, font=("Segoe UI", 10, "bold"))
        self.count.grid(sticky="w", pady=(4, 0))

        result = ttk.LabelFrame(top, text="Result so far", padding=8)
        result.grid(sticky="ew")
        self.result = ttk.Label(result, wraplength=840, font=("Segoe UI", 10, "bold"))
        self.result.grid(sticky="w")
        ttk.Label(result, wraplength=840, foreground="#57606a", text=f"Saved as you go to {self.log.path}").grid(
            sticky="w", pady=(4, 0))

    def _build_row(self, parent, i, step):
        button = ttk.Button(parent, width=24, command=lambda k=step.key: self.press(self._step(k)))
        button.grid(row=i, column=0, sticky="nw", padx=(0, 12), pady=5)
        text = ttk.Frame(parent)
        text.grid(row=i, column=1, sticky="ew", pady=5)
        does = ttk.Label(text, wraplength=540)
        does.grid(sticky="w")
        ttk.Label(text, text=f"Watch: {step.watch}", wraplength=540, foreground="#57606a").grid(sticky="w")
        answer = ttk.Label(text, wraplength=540)
        answer.grid(sticky="w")
        var = tk.StringVar()
        self.choice_vars[step.key] = var
        radios = []
        box = ttk.Frame(parent)
        box.grid(row=i, column=2, sticky="nw", padx=(12, 0), pady=5)
        for code, label in step.choices:
            r = ttk.Radiobutton(box, text=label, value=code, variable=var, state="disabled",
                                command=lambda k=step.key: self.observed(k))
            r.pack(anchor="w")
            radios.append(r)
        self.rows[step.key] = {"button": button, "does": does, "answer": answer, "radios": radios}

    # ---- state ------------------------------------------------------------
    def settings(self):
        return parse_settings(self.mm.get(), self.speed.get())

    def _step(self, key):
        if key == STOP.key:
            return STOP
        mm, speed = self.settings() or (DEFAULT_MM, DEFAULT_SPEED)
        return next(s for s in make_steps(mm, speed) if s.key == key)

    def refresh(self):
        settings = self.settings()
        mm, speed = settings or (DEFAULT_MM, DEFAULT_SPEED)
        ready = self.mode == IDLE
        for step in make_steps(mm, speed):
            row = self.rows[step.key]
            turning = step.data["action"] == "goto"
            why_not = None
            if turning and settings is None:
                why_not = f"Enter an amount of {MIN_MM:g} to {MAX_MM:g} units and a speed of {MIN_SPEED} to {MAX_SPEED} first."
            elif turning and not self.held:
                why_not = "Press Hold the motor first: it won't turn otherwise."
            row["button"].config(text="Turn (fix the numbers)" if turning and settings is None else step.button)
            row["does"].config(text=why_not or step.does)
            usable = ready and why_not is None and step.key not in self.pending
            row["button"].config(state="normal" if usable else "disabled")
        self.stop_button.config(state="normal" if self.mode else "disabled")
        self._draw_ruler(mm if settings else None)
        self.count.config(text=f"Turned since you pressed Hold: {self.total:g} of {UNITS_PER_CHUCK_TURN} units")
        self.result.config(text=verdict(self.seen))

    def _draw_ruler(self, mm):
        c = self.ruler
        c.delete("all")
        c.config(bg=self.root.cget("bg"))
        x0, x1, y = 16, 396, 34
        scale = (x1 - x0) / MAX_MM
        c.create_line(x0, y, x1, y, fill="#57606a")
        for n in range(int(MAX_MM) + 1):
            x = x0 + n * scale
            c.create_line(x, y - 3, x, y + 3, fill="#57606a")
            if n % 5 == 0:
                c.create_text(x, y + 14, text=f"{n}", fill="#57606a", font=("Segoe UI", 8))
        if mm is not None:
            x = x0 + mm * scale
            c.create_line(x0, y, x, y, fill=self.GOOD, width=3, arrow="last")
            c.create_text(min(max(x, x0 + 50), x1 - 50), y - 16, text=f"Turn {mm:g} units", fill=self.GOOD,
                          font=("Segoe UI", 9, "bold"))

    # ---- actions ----------------------------------------------------------
    def press(self, step):
        self.log.write(f"SENT {step.key}: {json.dumps(step.data)}")
        self.pending.add(step.key)
        self._set_answer(step, "Sending...", None)
        self.refresh()
        self.link.send(step)

    def observed(self, key):
        code = self.choice_vars[key].get()
        self.seen[key] = code
        self.log.write(f"OBSERVED {key}: {code}")
        self.log.write(f"RESULT SO FAR: {verdict(self.seen)}")
        self.refresh()

    def _set_answer(self, step, text, accepted):
        color = {True: self.GOOD, False: self.BAD, None: "#57606a"}[accepted]
        label = self.stop_answer if step.key == STOP.key else self.rows[step.key]["answer"]
        label.config(text=text, foreground=color)

    def _read_events(self):
        try:
            while True:
                self._handle(self.link.events.get_nowait())
        except queue.Empty:
            pass
        self.root.after(100, self._read_events)

    def _handle(self, event):
        kind = event[0]
        if kind == "status":
            self.mode = None
            self.log.write(f"status: {event[1]}")
            self.status.config(text=event[1], fg=self.WARN)
            self.refresh()
        elif kind == "mode":
            _, ip, mode = event
            if mode != self.mode:
                self.log.write(f"machine {ip} is {mode}")
            self.mode = mode
            text = f"Connected to {ip}. Machine is {describe_mode(mode)}."
            if mode != IDLE:
                text += " The test can't start until it is ready."
            self.status.config(text=text, fg=self.GOOD if mode == IDLE else self.WARN)
            self.refresh()
        elif kind == "answer":
            _, step, answer = event
            self.pending.discard(step.key)
            self.log.write(f"ANSWER {step.key}: {answer.raw or answer.text}")
            text = answer.text if step.key != STOP.key or not answer.accepted else "Machine accepted the stop."
            self._set_answer(step, text, answer.accepted)
            if answer.accepted and step.key != STOP.key:
                for r in self.rows[step.key]["radios"]:
                    r.config(state="normal")
            if answer.accepted:
                self._count(step)
            self.refresh()

    def _count(self, step):
        action = step.data["action"]
        if action == "lock":
            self.held, self.total = True, 0.0
        elif action == "free":
            self.held = False
        elif action == "goto":
            self.total += step.data["u"]
            self.log.write(f"TOTAL since Hold: {self.total:g} units")

    def _crashed(self, *exc):
        self.log.write("WINDOW ERROR: " + "".join(traceback.format_exception(*exc)))
        self.status.config(text=f"Something went wrong: {exc[1]!r}", fg=self.BAD)

    def close(self):
        self.log.write(f"window closed. RESULT: {verdict(self.seen)}")
        self.link.stop()
        self.root.destroy()


def main(argv=None):
    p = argparse.ArgumentParser(prog="python -m pcprobe.motor_test", description="Test the rotary motor outside a job.")
    p.add_argument("ip", nargs="?", help="machine address (found on the network if left out)")
    args = p.parse_args(argv)
    try:
        ctypes.windll.shcore.SetProcessDpiAwareness(1)  # sharp text on Windows; absent elsewhere
    except (AttributeError, OSError):
        pass
    root = tk.Tk()
    link = Link(args.ip)
    Window(root, link, ResultLog())
    root.lift()
    root.attributes("-topmost", True)  # open in front of whatever launched it, then behave normally
    root.after(800, lambda: root.attributes("-topmost", False))
    link.start()
    root.mainloop()


if __name__ == "__main__":
    main()
