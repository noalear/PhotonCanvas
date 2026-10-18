# Roadmap

Target machine: **xTool F2 Ultra** (`GS004-CLASS-4`, dual diode + fiber galvo).

## What the user wants fixed, and how

| Complaint | Root cause found | Plan |
|---|---|---|
| No live camera view | The stock app only takes snapshots (`/v1/camera/snap`) | Test `POST /v1/platform/camera/live` and the `media_stream` socket. If that fails, poll snapshots continuously and lens-correct them locally. |
| Can't pause between passes | Passes are a device-side `G104 P<n>`…`G105` loop in one file, and resuming needs the physical button | Option to emit each pass as its own job, with a "next pass" prompt (and optional changes between passes). |
| Inconsistent laser control | Settings go through profile/patch/binding layers, and the native planner is a black box | We generate our own G-code with explicit, visible parameters. A preview shows exactly what is sent. |
| Settings aren't explained | (none; UX problem) | Built-in docs per parameter, with units, the value actually sent to the device (e.g. power ×10, speed ×60), and effect. |
| Layers take manual work | Layers are just colour keys (`layerData`) | Real layer operations: bulk assign, presets, copying settings between layers, ordering. |
| Cloud, login, telemetry | Sentry, Sensors Analytics, GA, Firebase, network probes every 5–10 s | None of it. Fully offline, LAN only. |
| Slow / bloated | ~10 renderer processes, duplicated bundles, single-worker native server | A single-process app. |
| Unreliable jobs | (to be observed) | Explicit connection state, retries, and a logged protocol trace. |

## Phases

1. **Device link (CLI).** Discovery (V1 multicast + V2 AES UDP), WebSocket framing with CRC16, pairing, machineInfo, status, events. Proves we can talk to the machine.
2. **Capture ground truth.** Export `.xf` jobs from the stock app for simple test shapes (square cut, filled square, bitmap), at different power, speed and pass settings, for both blue and red sources. Diff them to learn the motion G-code dialect. Download the calibration files.
3. **Camera.** Snapshots, then test the live stream, then lens correction from `global_calib.json`.
4. **Job generation.** Our own G-code for vector cut/engrave, then fill, then bitmap. Package `xtool.xf`, upload over the file channel, framing, start/pause/stop, and pass-by-pass mode.
5. **Editor.** Canvas, `.xs` import/export, layers, settings with explanations.

Stack decision is open; see the end of the conversation that created this file.

## From comparing with LightBurn (2026-10-16/17)

All done:

- Changing text and codes: serial numbers, dates, CSV columns; QR / Data Matrix / barcodes.
- Depth slices for relief; scan angle turning per pass and between runs; offset fill; focus
  offset per layer; focus / line-spacing / any-two-settings test grids.
- Picture styles: halftone (cells per inch, angle), newsprint, line screen, sketch (edges);
  dot width correction on fills and slices.
- Shape tools: rounded corners, trim, text on a path, copy along a path, warp (taper, arch,
  bulge, wave), align by two points, measure, tidy lines (join, close, de-duplicate).
- Undo history window, font manager (star / hide), templates, lock single shapes, select circles
  by size, keyboard shortcut editor, G-code console with macros.
- Rotary and conveyor work modes, and marking big designs in sections (material moved by hand).

### Still to confirm on the machine

- **Rotary and conveyor** use the U axis exactly as xTool Studio's templates do (`M535U…`,
  `G100`, `G0 U0`, `G101`; see docs/gcode-f2-ultra.md), but two details are inferred: the
  `# gc=` header lines and where the galvo's other axis parks. One rotary job and one conveyor
  job exported from xTool Studio would settle both.
- **Console**: `POST /v1/cnc/cmd` with the command in the query parameters, as the stock app
  sends its pointer moves. Not yet tried.
- Everything new in G-code (focus offsets, turned passes, slices, offset fill, sections) is built
  from commands already verified, but hasn't been burned yet.

## Everything the others have that we don't (2026-10-17)

Compared against xTool Studio (from the installed copy: its modes, settings and menu text),
LightBurn (its documentation), EZCad (the usual galvo software) and others. `[x]` = done,
`[ ]` = to do. Items marked *online* need an account or a cloud service: they're listed so the
list is complete, but we stay offline, so only offline equivalents would be built.

### Layers and processing
- [x] Sub-layers: several operations on the same shapes (LightBurn sub-layers, EZCad stacked hatches)
- [x] One-way fill lines (xTool "one way", LightBurn bi-directional off)
- [x] Fill all of a layer's shapes in one scan (LightBurn fill grouping)
- [x] Perforation: dash and gap along cuts (LightBurn)
- [x] Dot mode along lines: one pulse every N mm (LightBurn dot mode)
- [x] Ramp for threshold pictures: sloped sides for stamps (LightBurn)
- [x] Pass-through pictures: the file's own pixels (LightBurn)
- [x] Cross hatch at any angle together with the main fill (xTool cross angle; we have 90° and turn-per-pass)
- [x] Dwell between scan lines against heat build-up (xTool "dwell time")
- [x] Outline after fill to hide ragged edges (xTool "outline tracing"; a sub-layer does it by hand)
- [x] Scan direction (top to bottom or bottom to top) and grouped fills (xTool)
- [x] Path order by layer, priority or as drawn; best start points and direction; remove overlapping lines automatically (LightBurn optimisation settings)
- [x] Lead-out, and starting loops at their nearest point (LightBurn)
- [x] Choose a shape's start point by clicking it (LightBurn)
- [x] "Evade smoke": an order that keeps away from the smoke plume (xTool)
- [ ] Same-direction processing for two-sided work (xTool)
- [x] Kerf that tells holes from parts when a drawing's directions are mixed (xTool odd-even kerf)
- [x] Tabs: click an outline to put a tab exactly there; tab cut power (xTool)
- [x] Framing: polygon frame, framing power and speed, framing that follows edits live (xTool)
- [x] Incise (xTool "Intaglio"): a vector fill carved down in slices with the focus following, no depth map
- [x] Relief: noise reduction (slices in reverse order) and more cleanup options (xTool)

### Machine modes
- [ ] Curved surface: measure the surface with the camera and the pointer (a grid of points), then follow it in Z (xTool "Curved material")
- [x] Screen printing: one job per layer with "next colour", mirrored design, CMYK split and halftone per colour (xTool "Screen preparation")
- [ ] Batch on the conveyor: photograph the items, find each one, put the design on it, mark, move on (xTool batch; their recognition is *online* or a downloaded model, ours would be our own shape matching)
- [ ] Variables batch: a different spreadsheet row on each item found (xTool)
- [ ] Quick processing: closing the lid focuses, photographs, finds the material, centres the design and starts (xTool)

### Shapes, text and editing
- [x] Simplify shapes: fewer points (LightBurn optimise, xTool simplify)
- [x] Cut overlapping shapes into pieces (LightBurn cut shapes)
- [x] Weld a text's letters together (LightBurn weld)
- [x] Select open lines, the same size, inside a shape, the same layers (LightBurn)
- [x] Round-object correction without a rotary (LightBurn cylinder correction)
- [x] Remove overlap between shapes (xTool)
- [x] Node types (sharp, smooth, symmetric), fillet a single corner, break at a point (xTool, LightBurn)
- [x] Extend lines to meet the next shape (LightBurn)
- [x] Scissors: cut a path at a point; auto-split (xTool)
- [x] Dashed lines and stroke width (xTool)
- [x] Shapes that stay editable: rectangle corner radius, polygon sides, star points after drawing (LightBurn)
- [x] Guidelines dragged from the rulers, with snapping (LightBurn, xTool)
- [x] Tangent circles from three circles (LightBurn)
- [x] Taper warp for cones (LightBurn)
- [x] Free-form warp: drag the four corners, mesh deform (LightBurn)
- [x] Text in a fixed-size box (shrinks to fit), fixed height, variable text that fits itself (xTool)
- [x] Single-line (stroke) fonts: one line per stroke, fast and clean on a galvo (xTool, LightBurn, EZCad)
- [x] Text shadow and 3D shadow effects (xTool)
- [x] Generators: finger-jointed boxes, keychains and tags (xTool; LightBurn by plugins)
- [x] Generators: living hinges, earrings
- [x] Multi-size copies (xTool)
- [ ] Map designer (xTool; needs map data, so likely *online*)
- [ ] Several canvases (pages) in one project (xTool)

### Pictures
- [x] Sharpen (LightBurn enhance, xTool sharpness)
- [x] Clip a picture to a shape (LightBurn apply mask, xTool clipping mask)
- [x] Crop by ratio or size (xTool)
- [x] Eraser (xTool)
- [x] Saturation and greyscale, picture filters: sketch, comic, emboss, grid, black-and-white (xTool)
- [x] Turn shapes into a picture (LightBurn convert to bitmap)
- [x] Centre-line tracing, and tracing by colour into layers (xTool)
- [x] CMYK split for screen printing (xTool)
- [x] 3D model (STL) to depth map; 3D preview of a relief (xTool)
- [ ] Lenticular pictures (xTool; probably not useful on a galvo)

### Files and workflow
- [x] Autosave and crash recovery; version history (xTool, LightBurn)
- [x] Paste pictures and SVG from other programs (LightBurn)
- [x] Export the design as SVG, DXF and PNG (LightBurn, xTool)
- [x] Import HPGL/PLT and LightBurn files (.lbrn2, .lbrn) (LightBurn)
- [ ] Import EPS (LightBurn; needs Ghostscript, which we don't ship)
- [x] Import options: shape by shape, grouped, every piece separate, or as one picture (xTool)
- [x] Settings backup and restore (LightBurn)
- [x] Job preview that plays the run with a time slider (LightBurn)
- [x] Job history and work log with times; a "job done" sound (xTool, LightBurn)
- [x] A light theme, inches (xTool, LightBurn); every button already has a tip
- [ ] Other languages (LightBurn, xTool)
- [ ] Movable, dockable panels (LightBurn)
- [ ] Mac and Linux builds (LightBurn)
- [ ] Other machines: the F1 Ultra and the rest of the family use the same commands (xTool)

### Material library and tests
- [ ] xTool's own tested F2 Ultra settings, imported from the installed copy when the app runs, never kept in our repo (xTool's live library is *online*)
- [x] Copy and paste layer settings (xTool)
- [x] Single-point test: one square at chosen settings (xTool)
- [ ] Read a burned test grid from a photo and point out the best square (xTool does it *online* with AI; ours would measure it, as Measure colours does)

### Machine and device
- [ ] Device settings page: name and versions, flame alarm and sensitivity, stop when moved, exhaust time after a job, sleep, laser overheat alarm (xTool)
- [ ] Galvo timing per laser: laser on/off delays, corner and jump delays (xTool, EZCad, LightBurn galvo)
- [ ] Calibrations: MOPA-to-blue alignment (engrave crosses, line them up on the camera), thickness measurement, focus, conveyor angle and X, pointer alignment (xTool)
- [ ] Focus: aimed measure (pick the point) and quick measure (xTool)
- [x] Z-axis homing (xTool)
- [ ] Back up a job to the machine, to run again from its screen (xTool)
- [ ] Resume after a power cut (xTool)
- [ ] Time-lapse video of a job from the camera (xTool)
- [x] Task and measurement history (xTool)
- [ ] Firmware updates from a file (xTool; downloading them is *online*)
- [ ] Wi-Fi setup and USB connection (xTool)
- [ ] Access key and room interlock, emergency-stop dialog, purifier and air-assist accessories (xTool)

### Online only (not planned; listed for completeness)
- Accounts and linking the machine to one; cloud projects and sharing; cloud fonts
- AI image generation and editing (AImake, cutout, enhance, expand, restyle, magic eraser), AI emboss, AI material recognition and suggested settings, AI test-grid reading, AI assistant
- Online design library and publishing; warranty lookup; telemetry
