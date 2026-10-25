# PhotonCanvas: architecture

How the program is put together, and how a design becomes a job on the machine. The machine's
side is described in [protocol-f2-ultra.md](protocol-f2-ultra.md) (network protocol),
[gcode-f2-ultra.md](gcode-f2-ultra.md) (the G-code it accepts) and [file-format.md](file-format.md)
(xTool project files PhotonCanvas can import).

## Projects

| Project | What it does |
|---|---|
| `src/PhotonCanvas.Core` | G-code writer, laser settings and machine limits, `.xf` job packaging, camera calibration, picture processing. No user interface. |
| `src/PhotonCanvas.Device` | The F2 Ultra link: discovery, the V2 WebSocket protocol, pairing, upload, camera pictures, machine settings, connection keys. |
| `src/PhotonCanvas.Doc` | The document: shapes, layers, undo/redo, imports, tracing, the job builder and checks, `.pcanvas` files, the material library. |
| `src/PhotonCanvas.App` | The desktop editor (Avalonia). |
| `src/PhotonCanvas.Server` | The remote-access server for the phone app (ASP.NET Core); see [remote-api.md](remote-api.md). |
| `src/PhotonCanvas.Remote*` | The phone app (shared UI, Android and a desktop test build) and its client library. |
| `tests/PhotonCanvas.Tests` | xUnit tests. They never need a machine or the real connection keys. |

## From a design to a job

1. **Document.** Shapes sit on layers; each layer says how it's processed (cut, score, fill,
   picture) and with which laser, power, speed and passes.
2. **Job builder.** Each layer's shapes are turned into paths: offsets and kerf, tabs, fill lines,
   dithered picture rows. Checks run here (out of the work area, missing settings, risky values).
3. **G-code.** The paths are written in the machine's G-code dialect, with the limits of the
   chosen laser applied.
4. **Package.** The G-code, a framing path, a preview and a short description go into an `.xf`
   file (a tar archive).
5. **Upload.** The file is sent to the machine with `autoStart: 0`: it waits there until someone
   presses the machine's button (or, if the user has turned it on, starts it from the phone; see
   [remote-api.md](remote-api.md)).

## Single process

The editor is one process. Heavy work (tracing, slicing, nesting, picture processing) runs on
background threads.
