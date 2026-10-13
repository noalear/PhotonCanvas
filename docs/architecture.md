# xTool Studio: architecture (as analyzed)

Version analyzed: xTool Studio 36.2.0 (`@atomm-studio/electron-main` 1.9.11, extensions 3.71.93).
Paths are relative to `reference/` (gitignored, regenerate with `tools/extract_asar.py`).

## Process layout

- **Electron main:** `app/.vite/build/main.js` → `app-D_Q460Hg.js`. Its jobs: windows and tabs, RPC to renderers, custom schemes (`atomm://`, `xtool://`), updater, ext sync, Sentry, network probes, and launching algo-server.
- **One renderer per app tab** (a separate `WebContentsView` each, with `sandbox:false, webSecurity:false`):
  - `shell` 36 MB
  - `editor` 99 MB (PixiJS canvas, three.js, OpenCV/Clipper WASM)
  - `home`, `projects`, `devices`, `materials`, `preview` 41 MB, `template-edit`, `firmware-update`, `system-settings`
  - internal dev tools that still ship: `tools-tester`, `device-mock-console`
- **Utility processes:** `usb-detect-worker`, `discover-worker` (LAN discovery).
- **algo-server** (native C++, see below).
- A SharedWorker and a process-manager window.

## Device extensions (`exts/`)

- `exts/manifest.json` maps a device code to `./<code>/index.js` and holds per-device connection config (protocol V1/V2, USB serial vs socket, WiFi socket, HTTP info parsers).
- Shared runtime: `exts/ext-runtime/16a7d4c988a3/xcs-ext-base.js` (10 MB). It holds the gcode pipeline and the algo plugin/function enums.
- Main copies exts into userData and verifies them against the sha256 digests in `bundle-manifest.json`. The editor dynamically imports a device's `index.js`.
- **F2 Ultra = `GS004-CLASS-4`**. `GS007-CLASS-4` is the F2 Ultra (Single) and `GS009-*` is the F2 Ultra UV.

## algo-server (native)

- **Launch:** `resources/tools/algo-server/algo-server-win32-x64.exe` is spawned with no argv and `stdio:'ignore'`. It is configured through env vars:
  - `ALGO_SERVER_PLUGIN_DIR`
  - `ALGO_SERVER_TOKEN` (32 random bytes, hex)
  - `ALGO_SERVER_MAX_WORKERS=1`
  - `ALGO_SERVER_ENDPOINT=\\.\pipe\algo-server-<pid>-<uuid>`
- **Transport:** HTTP/1.1 JSON over that named pipe, with `Authorization: Bearer <token>`.
  - `GET /api/v1/health` (main polls it every 200 ms, for up to 120 s)
  - `POST /api/v1/invoke {pluginName, functionName, inputJson, timeout, sessionId, createSession}`
  - `/api/v1/endSession`, `/api/v1/getManifest`, `/api/v1/listPlugins`
  - Responses are `{code, message, data}`. Code 499 means cancelled, 408 means timeout.
- **Implication:** the plugins can be driven as a black box from our own code during development (start the exe with these env vars, then POST to the pipe). This lets us compare our output with theirs.

### Plugins and the ops that matter for a laser job

| Plugin | Key ops | Need for F2 Ultra |
|---|---|---|
| `path_planning` | `genGCodeSync({dir,dst,manifest})`, `ComputeKerfOffsetSync`, `ExtendByBackLengthSync` (overcut), `SegmentOverlapOptimizationSync`, `GS004*` camera correct/measure, `DetectMarkSync` | **Core.** This is G-code generation. We will reimplement it. |
| `time_estimate` | `GCodeTimeEstimateSync` | Nice to have. Can be done by simulating our own G-code. |
| `graphic_image` | `bitmapCv`, `ColorSeparationSync`, `svgsJsonToPngSync`, `CorrectGraphicsSync` | Raster prep and camera-image correction. |
| `visual_perception` | `GS004AutoFocusSync`, `CalibCenterOffsetSync`, `InFerenceSync` (ONNX), fire detection | Optional: autofocus, AI. |
| `visual_sensing` | `StitchImageSync`, `HDRImageSync`, `CalibAutoSync`, `ManualCalibSync` | Camera calibration and stitching. Relevant to the live view. |
| `perception_3d` | cylinder/surface modelling | Only for curved or rotary jobs. |
| `dq`, `dt`, `js` | inkjet/DTF/UV PRN generation | Not relevant to us. |

Basic dithering, grayscale, Clipper offsets and potrace run in the renderer WASM (`renderer/static/wasm/EDITOR_LIBS_WASM*.wasm`), not in algo-server.

## Cloud and telemetry (all removable)

- **Sentry:** `sentry.xtool.com/103`, enabled in main and in about 43 renderer bundles, 20% tracing.
- **Sensors Analytics:** `dataaccess.makeblock.com/sa`, `dataaccess.mblock.cc/sa`, about 176 `track(` calls. Google Analytics is also present.
- **Firebase Cloud Messaging** push notifications.
- **Reachability probes** every 5 to 10 s: 1.1.1.1, 8.8.8.8, msftncsi, generate_204.
- **Region router, passport login, `api.xtool.com`, `s.xtool.com` CDN** (help images and assets).
- **Login is not required for local save.** It is only required for cloud space and AI tools.
- **Forced firmware-update app, electron-updater, cloud ext updates.**

## Why it feels slow

1. About 10 renderer processes plus main, 2 utility processes, algo-server and a SharedWorker. They added a custom tab freeze/tombstone system to cope with memory.
2. Every app is a separate Vite build, so large chunks are duplicated:
   - pixi ×4, heic2any ×5
   - a 3 MB shared UI chunk ×7
   - pathkit/lcms wasm ×6
   - about 300 MB of renderer assets in total
3. Huge single files: the editor router is 5.5 MB and `xcs-ext-base` is 10 MB.
4. algo-server has `MAX_WORKERS=1`, so every algorithm call is serialized. Data moves through temp files and JSON strings.
5. Constant background polling: network probe, process stats every 1.5 s, discovery, USB, telemetry. Background throttling is disabled.
