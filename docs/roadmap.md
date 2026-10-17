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

## To do (from comparing with LightBurn, 2026-10-16)

Done from that comparison: changing text and codes (serial numbers, dates, CSV columns, QR /
Data Matrix / barcodes), depth slices for relief, turning the scan angle per pass and between
runs, offset fill, focus offset per layer, and focus / line-spacing / any-two-settings test
grids. Still to do:

1. **More picture styles**
   - Halftone and Newsprint: dots that grow in the dark areas, with cell size (cells per inch) and angle.
   - Sketch: mark only the hard edges between light and dark (line drawings, handwriting).
   - Dot width correction: shorten each scan line by about half the beam width, so fills and pictures come out at their true size.
2. **Shape tools**
   - Rounded corners (fillet / radius) on selected corners or whole shapes.
   - Trim: cut a line back to where it crosses another.
   - Text on a path (any curve, not just the arc bend we have) and copy along a path.
   - Warp and deform (perspective, envelope).
   - Two-point align: move, turn and scale a shape so two of its points land on two chosen points.
   - Measure tool: distance and angle between two points on the work area.
   - Import cleanup: auto-join ends that nearly touch, close nearly-closed paths, delete duplicate lines (common in DXF and PDF files).
   - (Tabs/bridges already exist on cut layers.)
3. **Bigger than the work area, and round things.** Needs the machine's commands first, worked
   out from the reference software (study only) the way the MOPA limits were:
   - Split marking: a design bigger than 220 × 220 mm marked in sections, the piece moved between them.
   - Rotary (RA2 Pro and similar): marking around cylinders and rings.
   - Conveyor feeder: long pieces and batches.
4. **Smaller conveniences**
   - Undo history window (see every step, jump back to any).
   - Font manager (search, favourites, hide fonts).
   - Templates: save a design as a template; opening it makes a new file.
   - Lock single shapes (layers can already be locked).
   - Select circles by size (or exact size), for jigs with many holes.
   - Hotkey editor.
   - G-code console and macros, for advanced users and testing.
