<img src="src/PhotonCanvas.App/Assets/photoncanvas-512.png" alt="PhotonCanvas icon" width="96" align="right">

# PhotonCanvas

A free, offline, open replacement for xTool Studio for the **xTool F2 Ultra** laser.
No account, no cloud, no telemetry: designs and jobs stay on your computer and go
straight to the machine over your own network.

> **⚠ Safety and disclaimer: please read**
>
> Lasers can blind you, start fires and release toxic fumes. Never look at the beam or
> its reflections, never leave a job running unattended, ventilate, and only process
> materials you know are safe (no PVC or vinyl).
>
> This is independent hobby software. It is **not made, endorsed or supported by xTool
> or Makeblock**, and using it may affect your machine's warranty. It is provided
> **"as is", without warranty of any kind**. You use it entirely at your own risk; the
> authors are not liable for injury, fire, or damage to your machine, materials or
> property (GPL v3, sections 15–16).
>
> **Lid-open mode** ("Allow starting with the lid open" on the Machine tab) removes the
> protection the lid gives you. Only turn it on if everyone in the room wears safety
> glasses rated for 455 nm and 1064 nm, and never leave the machine unattended.

## Features

- Draw (rectangles, ellipses, polygons, stars, lines, pen, text), edit points, offset, weld/subtract, arrays, align, snapping and a configurable grid
- Import SVG, DXF, PDF/AI, xTool `.xs`/`.xcs` and pictures, and trace pictures into vectors
- Layers that cut, score, engrave areas, or engrave pictures (dithered), with blue diode and red fiber settings
- Material test generator (power × speed grid)
- Depthmaps for relief engraving with [DepthCharge](#depthcharge) (your own local DA3 / Depth Pro app), placed straight onto the design
- Calibrated overview camera laid over the work area, at any material height
- Draw from the camera: fit a design onto a real piece (move/turn/resize), pick a piece's outline with a magic wand (tolerance, smoothness, corners, speck filter), or trace edges with a magnetic pen
- Auto-focus, camera lights, beeper, job pause/resume/stop and progress
- Jobs are generated locally in the same G-code dialect as the stock software, checked line for line against real exports

Jobs are uploaded with `autoStart: 0`: nothing moves or fires until you press the
machine's start button.

## Getting started

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download), then run:
   ```bash
   dotnet run --project src/PhotonCanvas.App -c Release
   ```
2. On first start, read and accept the safety notice.
3. **Connection keys (one-time).** xTool machines expect three fixed codes when a
   program searches for them and pairs with them. They are not included in this
   project. If xTool Studio is installed on your computer, click **Find them in my
   xTool Studio** (Machine tab or the welcome screen) and they are read from your own
   copy in a few seconds and saved to `%APPDATA%\PhotonCanvas\keys.json`. You can also point
   it at xTool Studio's `resources\app.asar`, or paste them in; each one is checked
   against a fingerprint. Don't have xTool Studio? Install it, run the setup once, then
   uninstall it.
4. Turn the laser on. The Machine tab finds and connects to it; the first time, press
   the machine's button to allow this computer.

Help → Getting started shows the short tour again.

## DepthCharge

PhotonCanvas can turn a picture into a relief depthmap with **DepthCharge**, a separate
local app that runs depth models (DA3, Depth Pro) on your graphics card. Install DepthCharge
first (its `DepthCharge-Setup.exe`, or a set-up folder in Documents). Then right-click a
picture → **Depthmap with DepthCharge…** (or Tools, or the Picture card). PhotonCanvas starts
DepthCharge in the background if needed, talks to it on 127.0.0.1 only, and stops it again on
exit if it started it. Crops and masks made in DepthCharge's own editor (**Open in DepthCharge**)
are used too. DepthCharge is not part of PhotonCanvas; its models keep their own licences
(Depth Pro is non-commercial research only).

## Where things are kept

Everything lives in `%APPDATA%\PhotonCanvas`: `app.json` (settings and presets),
`camera.json` (camera calibration), `keys.json` (connection keys), `state.json`
(pairing token) and `logs\`.

## For developers

```
PhotonCanvas.slnx
src/PhotonCanvas.Core     G-code engine, settings/limits, .xf packaging, camera calibration, picture processing
src/PhotonCanvas.Device   F2 Ultra link: discovery, V2 WebSocket protocol, pairing, upload, camera, connection keys
src/PhotonCanvas.Doc      document model: shapes, layers, undo/redo, imports, tracing, job builder, .pcanvas files
src/PhotonCanvas.App      desktop editor (Avalonia)
tests/PhotonCanvas.Tests  xUnit; sample tests compare against captures/gc when present
src/ytool                 Python protocol prototype (read-only probe; reads the same keys.json)
docs/                     protocol, G-code and file-format notes
```

```bash
dotnet test
```

The tests never need the real connection keys or a machine.

### Ground rules

- `reference/` (gitignored) is for studying xTool Studio's behaviour and formats,
  for interoperability. Never copy its code into `src/` or commit anything from it.
- Never put the connection keys in the source, tests or docs.
- Nothing in the code may move the head or fire the laser without the user asking.

To regenerate `reference/` from your own install:

```bash
R="$LOCALAPPDATA/Programs/xTool Studio/resources"
python -I tools/extract_asar.py "$R/app.asar" reference/app
python -I -c "import zipfile,sys; zipfile.ZipFile(sys.argv[1]).extractall('reference/exts')" "$R/exts.zip"
```

## Licence

GNU General Public License v3 or later: see [LICENSE](LICENSE). You may use, share and
change it; if you distribute a changed version, it must stay under the same licence
with its source available. Third-party components and their licences are listed in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

xTool is a trademark of its owner, used here only to name the machines this works with.
