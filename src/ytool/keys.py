"""Machine keys, supplied by the user (the desktop app's Machine tab finds them in
the user's own xTool Studio and saves them to %APPDATA%/YTool/keys.json).

Environment variables override the file: YTOOL_DISCOVERY_REQUEST_KEY,
YTOOL_DISCOVERY_REPLY_KEY, YTOOL_PAIRING_KEY.
"""
import json
import os
from dataclasses import dataclass
from pathlib import Path


def _default_path() -> Path:
    base = os.environ.get("APPDATA") or str(Path.home() / ".config")
    return Path(base) / "YTool" / "keys.json"


@dataclass(frozen=True)
class MachineKeys:
    discovery_request: str
    discovery_reply: str
    pairing: str

    @property
    def request_bytes(self) -> bytes:
        return self.discovery_request.encode("ascii")

    @property
    def reply_bytes(self) -> bytes:
        return self.discovery_reply.encode("ascii")


def load_keys(path: Path | None = None) -> MachineKeys:
    data = {}
    p = path or _default_path()
    if p.exists():
        data = json.loads(p.read_text(encoding="utf-8"))
    keys = MachineKeys(
        os.environ.get("YTOOL_DISCOVERY_REQUEST_KEY", data.get("DiscoveryRequest", "")),
        os.environ.get("YTOOL_DISCOVERY_REPLY_KEY", data.get("DiscoveryReply", "")),
        os.environ.get("YTOOL_PAIRING_KEY", data.get("Pairing", "")),
    )
    if not (keys.discovery_request and keys.discovery_reply and keys.pairing):
        raise RuntimeError(f"Machine keys not set up: run the desktop app's Machine tab -> Connection keys (or create {p}).")
    return keys
