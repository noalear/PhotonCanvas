"""V2 protocol client for xTool F-series devices (wss://<ip>:28900/websocket).

See docs/protocol-f2-ultra.md for the protocol description.
"""
import asyncio
import hashlib
import json
import ssl
import time
import uuid
from pathlib import Path

from websockets.asyncio.client import connect

from .frame import FrameDecoder, ProtocolType, encode

PORT = 28900
USER_KEY = "<pairing-key>"  # base64("<pairing-key-text>"), sent as userKey
PING_TXID = 65510
FILE_REQUEST, FILE_DATA = 0x01, 0x81
WINDOW = 5 * 1024 * 1024
STATE_DIR = Path.home() / ".ytool"


class DeviceError(Exception):
    def __init__(self, code, message, data=None):
        super().__init__(f"device error {code}: {message}")
        self.code, self.message, self.data = code, message, data


def _load_state():
    try:
        return json.loads((STATE_DIR / "state.json").read_text())
    except (OSError, ValueError):
        return {}


def _save_state(state):
    STATE_DIR.mkdir(exist_ok=True)
    (STATE_DIR / "state.json").write_text(json.dumps(state, indent=2))


def _tls():
    # The device serves a self-signed certificate.
    ctx = ssl.create_default_context()
    ctx.check_hostname = False
    ctx.verify_mode = ssl.CERT_NONE
    return ctx


class Channel:
    """One framed WebSocket (instruction, file_stream or media_stream)."""

    def __init__(self, ip, conn_id, function, on_frame, trace=False):
        # All channels of one connection share conn_id; the device pairs them by it.
        self.url = f"wss://{ip}:{PORT}/websocket?id={conn_id}&function={function}"
        self.function = function
        self.on_frame = on_frame
        self.trace = trace
        skip = (ProtocolType.MEDIA_STREAM,) if function == "media_stream" else ()
        self.decoder = FrameDecoder(skip)
        self.ws = None
        self._reader = None

    async def open(self):
        self.ws = await connect(self.url, ssl=_tls(), max_size=None, ping_interval=None,
                                open_timeout=10)
        self._reader = asyncio.create_task(self._read())

    async def _read(self):
        try:
            async for msg in self.ws:
                data = msg.encode() if isinstance(msg, str) else msg
                for ptype, body in self.decoder.feed(data):
                    if self.trace:
                        preview = body[:200].decode("utf-8", "replace") if ptype == ProtocolType.JSON else body[:16].hex()
                        print(f"  <- [{self.function}] type={ptype} {len(body)}B {preview}")
                    await self.on_frame(ptype, body)
        except Exception as e:
            if self.trace:
                print(f"  [{self.function}] reader stopped: {e!r}")

    async def send(self, payload, ptype, body_crc=True):
        if self.trace:
            preview = payload[:200] if isinstance(payload, str) else payload[:16].hex()
            print(f"  -> [{self.function}] type={ptype} {preview}")
        await self.ws.send(encode(payload, ptype, body_crc))

    async def close(self):
        if self._reader:
            self._reader.cancel()
        if self.ws:
            await self.ws.close()


class Device:
    def __init__(self, ip, trace=False):
        self.ip = ip
        self.trace = trace
        self.state = _load_state()
        # The stock app sends "mk-guest" when not logged in. Firmware 40.54 drops the
        # connection for an unknown UUID userID, so guest is the default.
        self.user_id = self.state.get("userId", "mk-guest")
        self.session_token = ""
        self._txid = 0
        self._pending = {}
        self._ping_task = None
        self.events = asyncio.Queue()
        self.conn_id = str(uuid.uuid4())
        self.main = Channel(ip, self.conn_id, "instruction", self._on_main, trace)
        self.file = None
        self._downloads = {}
        self.media_frames = asyncio.Queue(maxsize=8)
        self.media = None

    # ---- connection -------------------------------------------------------
    async def connect(self, pair=True, on_button=None):
        await self.main.open()
        self._ping_task = asyncio.create_task(self._heartbeat())
        if pair:
            await self.pair(on_button)

    async def close(self):
        if self._ping_task:
            self._ping_task.cancel()
        for ch in (self.file, self.media, self.main):
            if ch:
                await ch.close()

    async def _heartbeat(self):
        while True:
            await asyncio.sleep(3)
            msg = {"type": "request", "method": "GET", "url": "/v1/user/ping",
                   "transactionId": PING_TXID, "data": {}, "params": {}, "timestamp": int(time.time() * 1000)}
            try:
                await self.main.send(json.dumps(msg), ProtocolType.JSON)
            except Exception:
                return

    async def _on_main(self, ptype, body):
        if ptype != ProtocolType.JSON:
            await self.events.put({"_binary": ptype, "len": len(body)})
            return
        try:
            msg = json.loads(body)
        except ValueError:
            return
        if isinstance(msg, dict) and msg.get("type") == "response":
            txid = msg.get("transactionId")
            if txid == PING_TXID:
                return
            fut = self._pending.pop(txid, None)
            if fut and not fut.done():
                fut.set_result(msg)
            return
        await self.events.put(msg)

    # ---- requests ---------------------------------------------------------
    async def request(self, method, url, data=None, params=None, timeout=10, raise_on_error=True):
        self._txid = self._txid % 65500 + 1
        msg = {"type": "request", "method": method, "url": url, "params": params or {},
               "data": data or {}, "timestamp": int(time.time() * 1000), "transactionId": self._txid}
        if self.session_token:
            msg["sessionToken"] = self.session_token
        fut = asyncio.get_running_loop().create_future()
        self._pending[self._txid] = fut
        await self.main.send(json.dumps(msg), ProtocolType.JSON)
        try:
            resp = await asyncio.wait_for(fut, timeout)
        finally:
            self._pending.pop(msg["transactionId"], None)
        if raise_on_error and resp.get("code") != 0:
            raise DeviceError(resp.get("code"), resp.get("message"), resp.get("data"))
        return resp if not raise_on_error else resp.get("data")

    async def get(self, url, **kw):
        return await self.request("GET", url, **kw)

    # ---- pairing ----------------------------------------------------------
    async def pair(self, on_button=None, button_timeout=90):
        tokens = self.state.setdefault("tokens", {})
        token = tokens.get(self.ip, "")
        tz = time.strftime("%Z")
        resp = await self.request("GET", "/v1/user/parity", raise_on_error=False, data={
            "userID": self.user_id, "userKey": USER_KEY, "timezone": tz, "token": token})
        code = resp.get("code")
        if code == 406:
            if on_button:
                on_button()
            deadline = time.time() + button_timeout
            while time.time() < deadline:
                await asyncio.sleep(1)
                resp = await self.request("GET", "/v1/user/auth-status", raise_on_error=False, timeout=5)
                code = resp.get("code")
                if code in (0, 410, 409, 503):
                    break
        if code != 0:
            raise DeviceError(code, resp.get("message") or "pairing failed", resp.get("data"))
        data = resp.get("data") or {}
        self.session_token = data.get("sessionToken", "")
        if data.get("token"):
            tokens[self.ip] = data["token"]
        _save_state(self.state)
        return data

    # ---- file channel -----------------------------------------------------
    async def _ensure_file_channel(self):
        if self.file is None:
            self.file = Channel(self.ip, self.conn_id, "file_stream", self._on_file, self.trace)
            await self.file.open()

    async def _on_file(self, ptype, body):
        if ptype != ProtocolType.FILE_TRANSFER or not body:
            return
        if body[0] == FILE_DATA and len(body) >= 7:
            ch = body[1]
            offset = int.from_bytes(body[2:7], "big")
            dl = self._downloads.get(ch)
            if dl:
                dl["buf"][offset:offset + len(body) - 7] = body[7:]
                dl["received"] += len(body) - 7
                dl["window_received"] += len(body) - 7
                dl["event"].set()

    async def download(self, filename, filetype, channel=1, timeout=30):
        await self._ensure_file_channel()
        info = await self.request("PUT", "/v1/filetransfer/download", data={
            "filetype": filetype, "filename": filename, "digesttype": 1,
            "channel": channel, "packetsize": 1024 * 1024})
        size = int(info["filesize"])
        dl = {"buf": bytearray(size), "received": 0, "window_received": 0, "event": asyncio.Event()}
        self._downloads[channel] = dl
        try:
            offset = 0
            while dl["received"] < size:
                window = min(WINDOW, size - offset)
                dl["window_received"] = 0
                pkt = bytes([FILE_REQUEST, channel]) + offset.to_bytes(5, "big") + window.to_bytes(3, "big")
                await self.file.send(pkt, ProtocolType.FILE_TRANSFER, body_crc=False)
                while dl["window_received"] < window:
                    dl["event"].clear()
                    await asyncio.wait_for(dl["event"].wait(), timeout)
                offset += window
        except Exception:
            await self.request("PUT", "/v1/filetransfer/finish", raise_on_error=False,
                               data={"code": -9, "message": "File transfer aborted", "channel": channel})
            raise
        finally:
            self._downloads.pop(channel, None)
        data = bytes(dl["buf"])
        digest = info.get("digestdata")
        if digest and hashlib.md5(data).hexdigest().lower() != str(digest).lower():
            raise DeviceError(-8, "MD5 mismatch on downloaded file")
        await self.request("PUT", "/v1/filetransfer/finish", raise_on_error=False,
                           data={"code": 0, "message": "file transfer finish", "channel": channel})
        return data

    # ---- camera -----------------------------------------------------------
    async def snapshot(self, stream=0):
        """stream 0 = overview camera, 1 = close-up. Returns JPEG bytes."""
        params = {"name": "deep" if stream else "main", "stream": str(stream)}
        res = await self.get("/v1/camera/snap", params=params, timeout=60)
        return await self.download(res["filename"], 5)

    async def open_media(self):
        if self.media is None:
            self.media = Channel(self.ip, self.conn_id, "media_stream", self._on_media, self.trace)
            await self.media.open()

    async def _on_media(self, ptype, body):
        if self.media_frames.full():
            self.media_frames.get_nowait()
        self.media_frames.put_nowait((ptype, body))
