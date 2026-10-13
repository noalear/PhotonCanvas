# YTool Studio

An independent, open replacement for xTool Studio (xTool laser cutter/engraver software).

## Layout

| Path | What | In git? |
|------|------|---------|
| `src/` | The new application (clean implementation) | yes |
| `docs/` | Our own notes: architecture, device protocols, `.xcs` format | yes |
| `tools/` | Helper scripts (e.g. `extract_asar.py`) | yes |
| `reference/` | Extracted xTool Studio install, for local study only | **no** (gitignored) |

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
