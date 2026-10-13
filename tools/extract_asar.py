"""Extract an Electron .asar archive without needing Node.

Usage: python -I tools/extract_asar.py <app.asar> <output_dir>
"""
import json
import os
import struct
import sys


def extract(asar_path, out_dir):
    with open(asar_path, "rb") as f:
        _, header_size, _, json_len = struct.unpack("<4I", f.read(16))
        header = json.loads(f.read(json_len).decode("utf-8"))
        base = 8 + header_size
        unpacked_dir = asar_path + ".unpacked"
        count = 0

        def walk(node, rel):
            nonlocal count
            for name, entry in node.get("files", {}).items():
                path = os.path.join(rel, name)
                dest = os.path.normpath(os.path.join(out_dir, path))
                if not dest.startswith(os.path.normpath(out_dir)):
                    raise ValueError(f"unsafe path in archive: {path}")
                if "files" in entry:
                    os.makedirs(dest, exist_ok=True)
                    walk(entry, path)
                elif "link" in entry:
                    continue
                else:
                    os.makedirs(os.path.dirname(dest), exist_ok=True)
                    if entry.get("unpacked"):
                        src = os.path.join(unpacked_dir, path)
                        if os.path.exists(src):
                            with open(src, "rb") as s, open(dest, "wb") as d:
                                d.write(s.read())
                    else:
                        f.seek(base + int(entry["offset"]))
                        with open(dest, "wb") as d:
                            d.write(f.read(entry["size"]))
                    count += 1

        walk(header, "")
    print(f"extracted {count} files to {out_dir}")


if __name__ == "__main__":
    extract(sys.argv[1], sys.argv[2])
