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
