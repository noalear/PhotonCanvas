"""LAN discovery for xTool devices (V2 encrypted UDP multicast, V1 legacy multicast)."""
import json
import os
import socket
import struct
import time

from cryptography.hazmat.primitives import padding
from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes

from .keys import load_keys

# Requests are encrypted with the discovery request key; replies are decrypted with
# the reply key, which is also sent inside the request body. Neither is built in:
# they are read from the keys file the desktop app saves (see keys.py).

V2_GROUPS = [
    ("224.0.0.251", 5353, 1),
    ("224.0.0.252", 5354, 1),
    ("239.0.1.251", 25353, 4),
    ("239.0.1.252", 25354, 4),
]
V1_GROUP = ("224.0.1.77", 20000)


def _key(k: bytes) -> bytes:
    return k[:32].ljust(32, b"\0")


def encrypt(data: bytes, key: bytes) -> bytes:
    iv = os.urandom(16)
    padder = padding.PKCS7(128).padder()
    enc = Cipher(algorithms.AES(_key(key)), modes.CBC(iv)).encryptor()
    return iv + enc.update(padder.update(data) + padder.finalize()) + enc.finalize()


def decrypt(blob: bytes, key: bytes) -> bytes:
    if len(blob) < 32:
        raise ValueError("too short")
    dec = Cipher(algorithms.AES(_key(key)), modes.CBC(blob[:16])).decryptor()
    unpadder = padding.PKCS7(128).unpadder()
    return unpadder.update(dec.update(blob[16:]) + dec.finalize()) + unpadder.finalize()


def local_ipv4s():
    ips = set()
    try:
        for info in socket.getaddrinfo(socket.gethostname(), None, socket.AF_INET):
            ips.add(info[4][0])
    except socket.gaierror:
        pass
    # Address of the default route interface.
    try:
        with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as s:
            s.connect(("8.8.8.8", 80))  # no packet is sent for UDP connect
            ips.add(s.getsockname()[0])
    except OSError:
        pass
    ips.discard("127.0.0.1")
    return sorted(ips)


def _bind_group_receiver(group, port, iface_ip):
    s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM, socket.IPPROTO_UDP)
    s.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    s.bind(("", port))
    mreq = struct.pack("4s4s", socket.inet_aton(group), socket.inet_aton(iface_ip))
    s.setsockopt(socket.IPPROTO_IP, socket.IP_ADD_MEMBERSHIP, mreq)
    s.setblocking(False)
    return s


def discover(timeout=4.0, target_ip=None, verbose=False):
    """Return a dict keyed by device IP with whatever each device reported."""
    keys = load_keys()
    found = {}
    socks = []
    ifaces = local_ipv4s()
    if verbose:
        print(f"[discover] local interfaces: {ifaces}")

    for ip in ifaces:
        s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM, socket.IPPROTO_UDP)
        try:
            s.bind((ip, 0))
        except OSError:
            s.close()
            continue
        s.setsockopt(socket.IPPROTO_IP, socket.IP_MULTICAST_IF, socket.inet_aton(ip))
        s.setblocking(False)
        socks.append((s, ip, "send"))
        for group, port, _ in V2_GROUPS:
            try:
                socks.append((_bind_group_receiver(group, port, ip), ip, f"rx{port}"))
            except OSError as e:
                if verbose:
                    print(f"[discover] can't listen on {group}:{port} via {ip}: {e}")

    def send_all():
        req_id = int.from_bytes(os.urandom(4), "big")
        v2 = json.dumps({"type": "deviceFind", "method": "request", "data": {
            "version": "1.0", "clientType": "atomnClient", "requestId": req_id,
            "key": keys.discovery_reply}}).encode()
        for s, ip, kind in socks:
            if kind != "send":
                continue
            local_port = s.getsockname()[1]
            v1 = json.dumps({"ip": ip, "port": local_port, "requestId": int(time.time() * 1000)}).encode()
            try:
                if target_ip:
                    for _, port, _ in V2_GROUPS:
                        s.sendto(encrypt(v2, keys.request_bytes), (target_ip, port))
                    s.sendto(v1, (target_ip, V1_GROUP[1]))
                else:
                    for group, port, ttl in V2_GROUPS:
                        s.setsockopt(socket.IPPROTO_IP, socket.IP_MULTICAST_TTL, ttl)
                        s.sendto(encrypt(v2, keys.request_bytes), (group, port))
                    s.setsockopt(socket.IPPROTO_IP, socket.IP_MULTICAST_TTL, 4)
                    s.sendto(v1, V1_GROUP)
            except OSError as e:
                if verbose:
                    print(f"[discover] send via {ip} failed: {e}")

    import select
    deadline = time.time() + timeout
    next_send = 0
    while time.time() < deadline:
        if time.time() >= next_send:
            send_all()
            next_send = time.time() + 1.5
        r, _, _ = select.select([s for s, _, _ in socks], [], [], 0.2) if socks else ([], [], [])
        for s in r:
            try:
                data, addr = s.recvfrom(65535)
            except OSError:
                continue
            if addr[0] in ifaces:
                continue
            info = _parse_reply(data, keys.reply_bytes)
            if info is None:
                if verbose:
                    print(f"[discover] unparsed {len(data)}B from {addr}")
                continue
            ip = info.get("deviceIp") or info.get("ip") or addr[0]
            found.setdefault(ip, {}).update(info)
            found[ip]["_from"] = addr[0]

    for s, _, _ in socks:
        s.close()
    return found


def _parse_reply(data: bytes, reply_key: bytes):
    # V2: encrypted JSON
    try:
        msg = json.loads(decrypt(data, reply_key))
        if msg.get("type") == "deviceFind" and msg.get("method") == "response":
            return {"protocol": "V2", **msg.get("data", {})}
        return None  # our own request echoed back, or another client's
    except Exception:
        pass
    # V1: plain JSON {ip, name, version, sncode, requestId}
    try:
        msg = json.loads(data.decode("utf-8"))
        if isinstance(msg, dict) and ("sncode" in msg or "name" in msg) and "requestId" in msg:
            return {"protocol": "V1", **msg}
    except Exception:
        pass
    return None
