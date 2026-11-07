import asyncio
import json
import os
import sys

import pytest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "src"))

import tkinter as tk

from pcprobe import keys
from pcprobe.motor_test import (CHECK_AMOUNT, CHECK_TOTAL, MAX_MM, MAX_SPEED, MIN_MM, STOP, Answer, Link, ResultLog, Step, Window,
                                make_steps, parse_settings, send_step, verdict)


class FakeDevice:
    def __init__(self, mode="P_IDLE", reply=None):
        self.mode, self.reply, self.sent = mode, reply or {"code": 0, "message": "Success"}, []

    async def get(self, url, **kw):
        assert url == "/v1/device/runtime-infos"
        return {"curMode": {"mode": self.mode}}

    async def request(self, method, url, data=None, params=None, timeout=10, raise_on_error=True):
        self.sent.append((method, url, data, params))
        if isinstance(self.reply, Exception):
            raise self.reply
        return self.reply


def test_steps_only_send_motor_commands_to_the_conveyor_type():
    dev = FakeDevice()
    for step in make_steps(5, 1200) + (STOP,):
        assert asyncio.run(send_step(dev, step)).accepted
    assert [s[2].get("action") for s in dev.sent] == ["lock", "goto", "free", "stop"]
    assert [s[2].get("u") for s in dev.sent] == [None, 5, None, None]
    assert {(s[0], s[1], s[3]["type"]) for s in dev.sent} == {("PUT", "/v1/peripheral/param", "conveyor")}


def test_anything_else_is_refused_before_it_reaches_the_machine():
    dev = FakeDevice()
    with pytest.raises(ValueError):
        asyncio.run(send_step(dev, Step("x", "x", "", "", {"action": "fire"})))
    assert dev.sent == []


@pytest.mark.parametrize("mm,speed", [(MAX_MM + 0.1, 1200), (0, 1200), (-5, 1200), (5, MAX_SPEED + 1), (5, 0)])
def test_limits(mm, speed):
    with pytest.raises(ValueError):
        make_steps(mm, speed)
    assert parse_settings(str(mm), str(speed)) is None


@pytest.mark.parametrize("mm,speed", [("x", "1200"), ("", "1200"), ("nan", "1200"), ("inf", "1200"), ("5", "")])
def test_junk_in_the_boxes_is_not_usable(mm, speed):
    assert parse_settings(mm, speed) is None


def test_no_motion_unless_idle_but_stop_always_goes():
    dev = FakeDevice(mode="P_WORK")
    for step in make_steps(5, 1200):
        answer = asyncio.run(send_step(dev, step))
        assert not answer.accepted and "busy (P_WORK)" in answer.text
    assert dev.sent == []
    assert asyncio.run(send_step(dev, STOP)).accepted
    assert dev.sent[0][2] == {"action": "stop"}


def test_sleeping_machine_is_named():
    answer = asyncio.run(send_step(FakeDevice(mode="P_SLEEP"), make_steps(5, 1200)[1]))
    assert answer.text == "Not sent: the machine is asleep."


def test_refusal_and_silence_are_reported():
    refused = asyncio.run(send_step(FakeDevice(reply={"code": -1, "message": "invalid type"}), STOP))
    assert not refused.accepted and "invalid type" in refused.text and json.loads(refused.raw)["code"] == -1
    silent = asyncio.run(send_step(FakeDevice(reply=asyncio.TimeoutError()), STOP))
    assert not silent.accepted and "No answer" in silent.text


def test_acceptance_is_not_reported_as_movement():
    assert "What did the chuck do?" in asyncio.run(send_step(FakeDevice(), make_steps(5, 1200)[1])).text


@pytest.mark.parametrize("seen,expected", [
    ({}, "Nothing recorded yet."),
    ({"turn": "unsure"}, "Nothing recorded yet."),
    ({"turn": "still"}, "No turn seen so far, even though the machine accepted the command."),
    ({"turn": "turned"}, "Yes: the motor turns outside a job."),
    ({"hold": "held", "free": "limp"}, "Hold works. Let go works."),
    ({"hold": "no_change", "free": "no_change"}, "Hold made no visible change. Let go made no visible change."),
])
def test_verdict(seen, expected):
    assert verdict(seen) == expected


def test_the_measuring_instructions_add_up_and_stay_within_the_limit():
    assert CHECK_TOTAL % CHECK_AMOUNT == 0 and MIN_MM <= CHECK_AMOUNT <= MAX_MM


def _window(tmp_path):
    root = tk.Tk()
    root.withdraw()
    window = Window(root, Link("192.0.2.1"), ResultLog(tmp_path))
    window._handle(("mode", "192.0.2.1", "P_IDLE"))
    return root, window


def _accepted(window, key):
    step = next(s for s in make_steps(8, 1200) if s.key == key)
    window._handle(("answer", step, Answer(True, "accepted", "{}")))


def test_turn_is_only_offered_while_the_motor_is_held_and_counts_from_hold(tmp_path):
    root, window = _window(tmp_path)
    try:
        turn = window.rows["turn"]["button"]
        assert str(turn["state"]) == "disabled" and "Hold" in window.rows["turn"]["does"]["text"]
        _accepted(window, "hold")
        assert str(turn["state"]) == "normal"
        _accepted(window, "turn")
        _accepted(window, "turn")
        assert window.total == 16 and "16 of 40" in window.count["text"]
        _accepted(window, "free")
        assert str(turn["state"]) == "disabled"
        _accepted(window, "hold")
        assert window.total == 0
    finally:
        root.destroy()


def test_log_is_written_as_it_goes(tmp_path):
    log = ResultLog(tmp_path / "new")
    log.write("first")
    log.write("second")
    lines = log.path.read_text(encoding="utf-8").splitlines()
    assert [l.split(" ", 1)[1] for l in lines] == ["first", "second"]


def test_pressing_a_step_before_connecting_says_so():
    link = Link("192.0.2.1")
    link.send(STOP)
    kind, step, answer = link.events.get_nowait()
    assert kind == "answer" and step is STOP and not answer.accepted and "Not connected" in answer.text


def test_keys_prefer_the_data_folder_in_home(tmp_path, monkeypatch):
    roaming = tmp_path / "roaming" / "PhotonCanvas"
    roaming.mkdir(parents=True)
    (roaming / "keys.json").write_text("{}")
    monkeypatch.setenv("APPDATA", str(tmp_path / "roaming"))
    monkeypatch.setattr(keys.Path, "home", lambda: tmp_path / "home")
    assert keys._default_path() == roaming / "keys.json"
    data = tmp_path / "home" / "PhotonCanvas"
    data.mkdir(parents=True)
    (data / "keys.json").write_text("{}")
    assert keys._default_path() == data / "keys.json"
