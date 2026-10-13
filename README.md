# YTool Studio

An independent, open replacement for xTool Studio (xTool laser cutter/engraver software).

## Layout

| Path | What | In git? |
|------|------|---------|
| `src/` | The application (C#) and the Python protocol prototype (`src/ytool`) | yes |
| `docs/` | Our own notes: architecture, device protocols, `.xcs` format | yes |
| `tools/` | Helper scripts (e.g. `extract_asar.py`) | yes |
| `reference/` | Extracted xTool Studio install, for local study only | **no** (gitignored) |

## The app (C# / .NET 10 + Avalonia)

```
YTool.slnx
src/YTool.Core     G-code engine, settings/limits, .xf packaging (matches stock exports line for line)
src/YTool.Device   F2 Ultra link: discovery, V2 WebSocket protocol, pairing, upload, camera
src/YTool.Doc      document model: shapes, layers, undo/redo, SVG import, job builder, .ytool files
src/YTool.App      desktop editor (Avalonia)
tests/YTool.Tests  xUnit; sample tests compare against captures/gc when present
```

```bash
dotnet test
dotnet run --project src/YTool.App
```

Jobs are uploaded with `autoStart: 0`: nothing moves or fires until the machine's start button is pressed.

## Quick start (Python device probe)

```bash
python -m venv .venv && .venv/Scripts/pip install websockets cryptography pillow
cd src
../.venv/Scripts/python -m ytool discover
../.venv/Scripts/python -m ytool info <ip>
../.venv/Scripts/python -m ytool view <ip>            # live camera window (~4 fps)
../.venv/Scripts/python -m ytool snap <ip> --stream 1  # close-up camera still
../.venv/Scripts/python -m ytool watch <ip> --poll 5   # device events + progress
```

The probe is read-only. Nothing in it moves the head or fires the laser.

## Ground rules

`reference/` holds xTool/Makeblock's proprietary code. Study it to learn protocols
and file formats (interoperability), then write our own implementation in `src/`.
Don't paste their code into `src/` or commit anything from `reference/`.

## Regenerating `reference/`

```bash
R="$LOCALAPPDATA/Programs/xTool Studio/resources"
python -I tools/extract_asar.py "$R/app.asar" reference/app
python -I -c "import zipfile,sys; zipfile.ZipFile(sys.argv[1]).extractall('reference/exts')" "$R/exts.zip"
```

Analyzed version: xTool Studio 36.2.0 (extensions 3.71.93, electron-main 1.9.11).
