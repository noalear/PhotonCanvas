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
- [x] Stacked slices: a 3D model (STL, OBJ) sliced into layers of the material's thickness, any way up; middle, outer (sand back) or inner (fill) outlines; each part numbered from the bottom, the next slice's place marked on it, optional dowel holes; laid out bed by bed on pages (our own idea; Autodesk's discontinued Slicer for Fusion 360 did the slicing, no laser software does)
- [x] Stacked slices: edges shaped to the model's slopes by engraving each slice's top down in steps of the measured etch depth (a "Slices · shaping" layer, before the cut); undersides (where the model narrows downward) are counted, to sand. The slicer also takes its cut settings, kerf and slot fit from the library's cut-test results
- [x] STEP (.stp, .step) CAD files for the slicer, the model browser and depth reliefs: our own reader turns the exact shapes (planes, cylinders, cones, spheres, tori including spindle ones, swept curves, NURBS) into sealed meshes within 0.05 mm; assemblies placed, inch files in millimetres; no CAD engine to install
- [x] Stacked slices: a model browser with shaded thumbnails of every STL and OBJ in a folder (Windows shows none for them)
- [x] Egg crate: upright sheets across the model both ways, slotted half-way where they cross; each sheet named and every slot marked with the sheet that goes in it; slot fit setting
- [x] Radial: fins round an upright axis slotted onto flat rings, with an optional centre hole for a dowel; preview shows the sheets standing in place
- [x] Slices go on pages of their own (named after the model) when the current page already has something on it
- [x] Slices and sheets nested by true shape (biggest first, quarter turns, small parts in the gaps) to fill each bed before starting the next; nesting itself much faster
- [x] Material outline: any closed shape (drawn, or picked from the camera) marked as the board in the machine goes on a Material layer that's never sent; Nest and 3D to slices fill it (holes kept clear, around what's already there), further beds repeat it on new pages
- [x] Slicer preview: burnt edges (charred cut edges per material, darkest where the beam enters, smoke beside the cut; acrylic stays clean)
- [x] Cut test: squares at speeds × passes (or power), click the ones that dropped, the quickest is kept; then a kerf square and slot-fit comb at that setting; kerf and slot fit saved with the material. Labels on tests and slicer marks now run before any cut
- [x] Curved surface: measure it with the pointer and the close-up camera (up to 40 × 40 points, bad points found and left out), or type in a cylinder or slatted drum; fit as any shape, cylinder, drum of flat slats or slanted plane; 3D preview; marked in 0.2 mm height bands as xTool Studio does (xTool "Curved material")
- [x] Screen printing: one job per layer with "next colour", mirrored design, CMYK split and halftone per colour (xTool "Screen preparation")
- [x] Batch on the bed: photograph the pieces, find each one, put a copy of the design on it, turned and snapped to its edges (xTool batch; their recognition is *online* or a downloaded model, ours is our own piece finding)
- [ ] The same on the conveyor: mark, move on, photograph again (needs the conveyor)
- [x] Variables batch: a different serial number or spreadsheet row on each item found, or on each copy of an array (xTool)
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
- [x] Several canvases (pages) in one project (xTool)

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
- [x] Updates for friends: tools/release.sh builds every platform and publishes a release on the public, releases-only repo (noalear/PhotonCanvas-releases; the source stays private); at start the app asks GitHub for the latest release and offers it (Settings → Updates: off, or check now)
- [x] Autosave and crash recovery; version history (xTool, LightBurn)
- [x] Paste pictures and SVG from other programs (LightBurn)
- [x] Export the design as SVG, DXF and PNG (LightBurn, xTool)
- [x] Import HPGL/PLT and LightBurn files (.lbrn2, .lbrn) (LightBurn)
- [x] Import EPS and PostScript (LightBurn), through Ghostscript when it's installed (we don't ship it)
- [x] Import options: shape by shape, grouped, every piece separate, or as one picture (xTool)
- [x] Settings backup and restore (LightBurn)
- [x] Job preview that plays the run with a time slider (LightBurn)
- [x] Job history and work log with times; a "job done" sound (xTool, LightBurn)
- [x] A light theme, inches (xTool, LightBurn); every button already has a tip
- [ ] Other languages (LightBurn, xTool)
- [x] Movable, dockable panels (LightBurn): layers, selection and layer settings move between the sides, float in their own windows or fold to their titles; kept between runs (View → Reset panels)
- [x] Mac and Linux builds (LightBurn): `tools/publish.sh`; built and packaged, not yet run on a Mac or Linux machine
- [x] User-defined machines (Settings → Machines): GRBL machines added and described (bed, laser, speeds, power scale, origin, port, air assist), with a Full Spectrum 5th Gen Hobby 20×12 starting point; jobs written as GRBL 1.1 laser-mode G-code and streamed over USB (character-counting flow control), with home, unlock, frame, pause, stop and Save G-code. Not yet run on a real GRBL machine
- [ ] Other machines: the F1 Ultra and the rest of the family use the same commands (xTool)

### Material library and tests
- [x] xTool's own tested F2 Ultra settings, and the user's own from xTool Studio: Library → Import from xTool Studio reads its cache (Chromium IndexedDB: LevelDB, Snappy, V8 values) read-only, materials and settings only; nothing kept in our repo
- [x] Copy and paste layer settings (xTool)
- [x] Single-point test: one square at chosen settings (xTool)
- [x] Etch-rate test: circles burned 1, 2, 3 … passes, round by round; the first through gives thickness ÷ passes with its range, depths on shallower ones show the slowdown; saved with the material (our own)
- [x] Read a burned test grid from a photo and point out the best square: darkest even mark or lightest clear one, measured against the bare material (xTool does it *online* with AI)

### Machine and device
- [x] Device settings page: name and versions, job counts, flame alarm, stop when moved, stop when the lid opens, exhaust run-on, sleep, MOPA overheat alarm, beeper (xTool; the F2 Ultra has no flame sensitivity setting)
- [x] Galvo timing per laser: laser on/off delays, corner and jump delays, factory reset (xTool, EZCad, LightBurn galvo)
- [ ] Calibrations: MOPA-to-blue alignment (engrave crosses, line them up on the camera), thickness measurement, focus, conveyor angle and X, pointer alignment (xTool)
- [ ] Focus: aimed measure with the camera and the red pointer (xTool's fallback; the F2 Ultra has no separate quick-measure sensor)
- [x] Z-axis homing (xTool)
- [x] Keep a job on the machine, to run again from its screen (xTool; needs the touchscreen controller)
- [x] Resume after a power cut: offered on connecting (xTool)
- [x] Time-lapse of a job from camera photos, made into a video with ffmpeg when it's installed (xTool has the words for it but not the feature on the F2 Ultra)
- [x] Task and measurement history (xTool)
- [ ] Firmware updates from a file (xTool; downloading them is *online*)
- [x] Wi-Fi setup (scan, join) and finding the machine over a USB or Ethernet cable (xTool)
- [x] Interlock status (lid, safety key, exhaust fan, emergency stop) and alarm messages for the emergency stop, flame, tilt and overheating (xTool)
- [ ] Purifier and duct-fan accessory control (fan speed) (xTool)

### New ideas nobody else has (researched 2026-10-19)

- [x] Photo engraving matched to the material (Laser → Tone test; the correction goes with a picture layer's settings and into the library): burn a grey step wedge, the camera reads how dark each step came out, and pictures are corrected so their greys come out as they look; a preview of how it will look (Trotec has you read a matrix by eye; Bambu builds a curve; none read it with the machine's own camera)
- [x] Heat-aware fill order (layer setting "Spread the lines out", fills and pictures): fill lines burned spread apart (1, 5, 9 … then 2, 6, 10 …) so neighbours never follow each other: less scorching and warping, more even MOPA colours (heat build-up from adjacent passes is what laser-processing research warns about; cheap on a galvo)
- [x] Engrave to an exact depth (Laser → Exact depth; rounds you ship as usual, measured in between): measure the depth with the red pointer between passes and adjust the passes left (only in patents so far). Needs the pointer's real precision measured at the machine test
- [ ] Engraving-style portraits: photos redrawn as banknote lines that swell in the shadows: straight, contour-following or guilloché (only separate web tools do this)
- [ ] Inlay and marquetry maker: matching pocket and piece from one design, the fit from the cut test's kerf and the pocket depth from the etch rate so veneer sits flush
- [x] Stencil bridges: the middles of O, A, R kept in by automatic bridges (Outline → More… → Stencil bridges)
- [ ] Laser-induced graphene (experimental): burn cork or wood into conductive carbon for touch pads and simple sensors, with a resistance test pattern; whether the 450 nm diode does it needs testing (papers show hobby visible lasers on cork)
- [ ] Print-then-cut: the camera finds printed artwork (stickers) and the diode cuts round it (Bambu's H2D sells on this)

### Online only (not planned; listed for completeness)
- Accounts and linking the machine to one; cloud projects and sharing; cloud fonts
- AI image generation and editing (AImake, cutout, enhance, expand, restyle, magic eraser), AI emboss, AI material recognition and suggested settings, AI test-grid reading, AI assistant
- Online design library and publishing; warranty lookup; telemetry

### To check at the machine test

Built from xTool Studio's own requests but not yet tried on the machine:
- Device settings: every switch reads back what the machine shows; renaming; exhaust run-on.
- Galvo timing: values read match xTool Studio's (shown ×10); saving one value; factory reset.
- Power-cut resume: the offer appears after pulling the plug mid-job (scrap material), and Carry on waits for the button.
- Keep a job on the machine (needs the touchscreen controller).
- Wi-Fi scan and join; finding the machine over a USB cable.
- Interlock status; the alarm messages (emergency stop is the safe one to try).
- Place on every piece: pieces found on the real bed; the bed's honeycomb not mistaken for pieces.
- Cut order, start points, tabs, lead-out, perforation and dot mode on scrap.
- Time-lapse: photos while a job runs don't disturb it.
- Curved surface: calibrate the pointer on a flat board (are the photos dark enough to see the dot? xTool lowers the camera exposure); measure the slatted drum at 20 × 20; mark a test on it; check the job is accepted as CURVE_PROCESS.
- Read the squares: on a real burned test grid.
- Etch rate: a 3 mm board, 30 circles; does the through-hole show clearly enough to pick the first one?
- Egg crate / radial: cut a small set in 3 mm basswood; do the slots slide together at 0.1 mm fit with the cut layer's kerf?
- Material outline: outline a real offcut with Pick piece, Use as material outline, slice into it; do the parts land on the wood?
- Cut test: does a cut square drop cleanly or need a push; is the measured kerf about 0.1–0.2 mm on 3 mm basswood; does the comb's snug slot match the egg crate fit?
- Spread lines: confirmed 2026-10-20, the machine takes picture rows out of order. On basswood pictures, though, spreading leaves visible rows (the Line test). Still to compare: scorching on thin acrylic and colour evenness on steel, with it on and off.
- Picture rows at an angle (scan angle, turn per pass, crosshatch): confirmed 2026-10-20, the machine takes dot-by-dot rows at 20–30° (Line test patches). Still to check: whether the edges line up between turned passes.
- A finished job put away before the next upload (P_WORK_DONE → P_IDLE without the button): still to check.
- Tone test: does the camera photo of a burned strip read cleanly (lighting, glare, grain)? Burn a portrait with and without the correction on basswood and compare.
- Exact depth: how precise is the pointer at reading a hole's floor (repeat a reading 5 times; compare with calipers' depth gauge)? Does the dot show on charred wood? Try 1 mm in basswood and 0.3 mm in steel.
