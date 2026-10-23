# Glossary

The words you'll meet in PhotonCanvas and in this guide, each with a short meaning and where it's
explained in full.

## A–C

- **Active layer**: the layer new drawings and imports go on. Click a layer to make it active;
  selecting shapes that are all on one layer does it too. See [Layers](layers.md).
- **Auto-focus**: the machine measures the material under the head and moves to the right focus
  height. In the machine panel at the right end of the ribbon. See [The machine and settings](machine.md).
- **Calibration (camera)**: lining the camera picture up with where the laser marks, once per
  material height. See [Camera](camera.md).
- **Close-up camera**: the camera in the laser head, for sharp pictures of a small area: used to
  read tests and line up the lasers. See [Camera](camera.md).
- **Code**: a QR code, Data Matrix or barcode on the design. It can hold a serial number, the date
  or a spreadsheet column. See [Drawing](drawing.md).
- **Compound shape**: one shape made of several outlines (**Combine**); where they overlap, they
  make holes. See [Editing shapes](editing-shapes.md).
- **Connection keys**: three codes xTool machines expect before they'll talk to a program. Set up
  once, from your own xTool Studio. See [Getting started](getting-started.md#connecting-to-the-machine)
  and [The machine and settings](machine.md).
- **Crosshatch**: each pass of a fill (or picture) run twice, the second at an angle to the first,
  for a more even result. See [Layer settings](layer-settings.md).
- **Cut** (layer mode): follows each outline at cutting settings, to cut through. See
  [Layer settings](layer-settings.md).
- **Cut test**: small squares cut at different speeds and passes, to find what cuts through; then
  the kerf and the slot fit at that setting. See [Materials and tests](materials-and-tests.md).

## D–F

- **Depth slices**: a picture mode for relief: the picture is a depth map (dark = deep), engraved
  as thin slices one after another, each covering everything at least that deep. See
  [Layer settings](layer-settings.md).
- **Depthmap**: a grey picture where darkness means depth, for relief engraving. Picture →
  **Depthmap** makes one with DepthCharge, a separate program. See [Pictures](pictures.md).
- **Diode** (**Diode · blue**): the 40 W, 455 nm blue laser. A layer uses it unless it's set to
  **MOPA**. See [Layer settings](layer-settings.md).
- **Dithering**: how the grey tones of a picture become dots, each burned at full power (Jarvis,
  Floyd–Steinberg, Stucki, Atkinson, Bayer, Threshold, Halftone, Newsprint, Line screen, Sketch).
  See [Layer settings](layer-settings.md).
- **Dot mode**: a single laser pulse every so many millimetres along a line instead of a
  continuous line. See [Layer settings](layer-settings.md).
- **dpi** (**Resolution**): dots per inch of an engraved picture. Higher is finer and slower;
  150–300 suits most wood. See [Layer settings](layer-settings.md).
- **Engrave** (layer mode): fills the inside of closed shapes with scan lines, holes left clear.
  See [Layer settings](layer-settings.md).
- **Etch rate**: how deep one pass goes in a material, from circles burned 1, 2, 3 … passes. See
  [Materials and tests](materials-and-tests.md).
- **Field**, **work area**: the area the laser can reach without moving the material: 220 × 220 mm
  on the F2 Ultra, the square on the canvas. Its top-left corner is 0, 0. See
  [Working on the canvas](canvas.md#the-work-area).
- **Fill**: another word for engraving an area (the **Engrave** mode).
- **Focus**, **focus distance**: the beam is narrowest at one distance below the head; the focus
  distance is the gap between the head and the material. **Auto-focus** sets it;
  **Focus offset (mm)** moves it for one layer. See [The machine and settings](machine.md) and
  [Layer settings](layer-settings.md).
- **Frame**, **framing**: before a job, the head traces around it with a faint laser dot, over and
  over, to show where it will land. See [Running a job](running-jobs.md).
- **Frequency** (MOPA): how many pulses per second, in kHz. Higher overlaps the pulses more (smoother
  lines, less energy in each). Below its full-power frequency for the pulse width, the source gives
  less than its full power. See [Layer settings](layer-settings.md).

## G–K

- **Galvo**: a laser head that steers the beam with two swinging mirrors instead of moving the
  head about. It's fast, and the head stays put over the whole field. Away from the middle of the
  field the beam comes down at a slight slant (see **Bevel test** in
  [Materials and tests](materials-and-tests.md)).
- **G-code**: the lines of text commands a job is made of: where to move, how fast, at what power.
  PhotonCanvas writes it on your computer, in the same dialect xTool Studio uses. **Export G-code**
  saves it as text. See [Running a job](running-jobs.md).
- **Grid**: lines on the work area to draw against, every 10 mm and 1 mm to start with. See
  [The grid](canvas.md#the-grid).
- **Group**: shapes that are selected and moved together. `Alt`+click picks one out of it. See
  [Editing shapes](editing-shapes.md).
- **Guide**: a line dragged out of a ruler to line shapes up against. Never sent. See
  [Guides](canvas.md#guides).
- **Halftone**: a dithering that turns greys into dots of different sizes on a regular screen
  (cells per inch, screen angle). See [Layer settings](layer-settings.md).
- **Highly reflective material**: a layer setting for polished metals and the like. The MOPA then
  keeps out of the frequencies where light bouncing back could harm it. See
  [Layer settings](layer-settings.md).
- **Image mask**: a closed shape that a picture shows through: only the part inside is shown and
  engraved, and only that part has to fit the work area. See [Pictures](pictures.md#image-mask).
- **Job**: what's sent to the machine: every visible layer with output on, of the page on screen,
  in order. The job bar shows it. See [Running a job](running-jobs.md).
- **Kerf**: the width of the cut line. The **Kerf (mm)** setting moves outlines out by half of it,
  so parts come out at the size drawn (and holes stay true). The cut test measures it. See
  [Layer settings](layer-settings.md).

## L–O

- **Layer**: a set of shapes processed with the same settings. Every shape is on one. Layers run
  top to bottom. See [Layers](layers.md).
- **Lead-in**, **lead-out**: a short line into the waste before an outline starts, and out of it
  after it ends, so the start and end marks aren't on the part. See [Layer settings](layer-settings.md).
- **Library** (material library): settings that worked for each material, with photos; shared as
  `.pcmat` files. See [Material library](material-library.md).
- **Lines / cm**: how densely an area is scanned. More lines: darker, smoother, slower. See
  [Layer settings](layer-settings.md).
- **Locked**: a locked shape can't be picked or moved on the canvas, but is still marked. A locked
  layer's shapes can't be picked. See [Locked shapes](canvas.md#locked-shapes).
- **Material outline**: a closed shape marked as the board in the machine. It goes on the
  Material layer, which is never sent, and **Nest** and **3D to slices** fill it. See
  [Arranging](arrange.md).
- **MOPA**: the 60 W, 1064 nm fiber laser. Its pulse width and frequency can be set, which lets it
  mark metal in many ways, including colours on stainless steel and titanium. See
  [Layer settings](layer-settings.md).
- **Nest**: packing copies or parts onto the work area or a piece as tightly as they fit. See
  [Arranging](arrange.md).
- **Nudge**: moving the selection with the arrow keys: 1 mm, 10 mm with `Shift`, 0.1 mm with
  `Ctrl`. See [Moving, resizing and turning](canvas.md#moving-resizing-and-turning).
- **Offset**: a new outline a set distance outside or inside a shape. See
  [Editing shapes](editing-shapes.md).
- **Output** (layer): whether a layer is sent to the laser (the lightning icon). Off: shown as a
  guide, never sent. See [Layers](layers.md).
- **Overcut**: carrying on cutting a little past the start, so the outline closes cleanly. See
  [Layer settings](layer-settings.md).
- **Overscan**: each scan line starts and ends a little outside the shape, with the laser off, so
  the edges burn evenly. See [Layer settings](layer-settings.md).

## P–R

- **Page**: a project can hold several pages, each its own design and its own job. See
  [The pages bar](workspace.md#the-pages-bar).
- **Passes**: how many times a layer is processed. **Round by round** runs every layer's first
  pass, then every layer's second pass, and so on; **Path by path** runs all the passes of each
  path before the next. See [Running a job](running-jobs.md).
- **Picture** (layer mode): engraves pictures, as dots (dithered), in greyscale or as depth
  slices. See [Layer settings](layer-settings.md) and [Pictures](pictures.md).
- **Pointer**: the machine's red pointer, a visible dot. PhotonCanvas uses it with the close-up
  camera to measure heights (aimed focus, curved surfaces, exact depth). See [Camera](camera.md).
- **Power %**: the share of the laser's full power, 0 to 100. See [Layer settings](layer-settings.md).
- **Processing order**: the order the paths run in, shown on the work area from blue (first) to
  red (last). See [Processing order](canvas.md#processing-order).
- **Pulse width** (MOPA): how long each laser pulse lasts, from 2 to 500 ns. Short pulses mark
  crisply; long pulses put in more heat. See [Layer settings](layer-settings.md).
- **Quick processing** (**Quick on lid close**): closing the lid focuses on the material,
  photographs the bed, centres the design on the piece and loads the job; the machine's button
  still starts it. Not yet tried on the machine. See [Running a job](running-jobs.md).
- **Relief**: engraving a picture as a carved surface rather than shades, from a depth map. See
  **Depth slices**, and [Pictures](pictures.md).

## S–T

- **Scan angle**: the direction an area's scan lines run; 0 = left to right. See
  [Layer settings](layer-settings.md).
- **Score** (layer mode): follows each outline lightly, to mark a line rather than cut through.
  See [Layer settings](layer-settings.md).
- **Sections**: marking a design bigger than the work area one part at a time, moving the
  material between parts. See [Work area](work-area.md).
- **Split onto pieces**: one design marked across several separate pieces that go together (four
  boards making one picture), a page each, lined up by the camera. See [Work area](work-area.md#split-onto-pieces).
- **Object snap**: a picked point landing exactly on another shape's end, middle, centre, quadrant
  or crossing (or perpendicular or tangent to it), with a marker saying which, as in AutoCAD. See
  [Object snaps](canvas.md#object-snaps).
- **Ship It**: uploads the job to the machine. Nothing moves or fires until you press the
  machine's start button. See [Running a job](running-jobs.md).
- **Snap**, **snapping**: dragged shapes jumping into line with other shapes, guides, the work
  area's edges and centre lines, or the grid. `Ctrl` while dragging turns it off. See
  [Snapping](canvas.md#snapping-the-grid-and-guides).
- **Speed**: how fast the beam moves while firing, in mm/s. Slower is deeper and darker. See
  [Layer settings](layer-settings.md).
- **Spread the lines out**: a fill and picture setting that burns lines in an order where
  neighbours never follow each other, for less heat build-up. See [Spread the heat](heat-order.md).
- **Step down**: lowering the head a little before each pass after the first, so the focus
  follows a deep cut down. See [Layer settings](layer-settings.md).
- **Sub-layer**: another operation on the same shapes as a layer, with its own settings (an
  engrave and then a cut, for example). See [Layers](layers.md).
- **Tabs**: small uncut bridges left in an outline so the part stays in the sheet. See
  [Layer settings](layer-settings.md).
- **Test grid**: a grid of squares burned at different settings (power × speed, focus, line
  spacing or any two settings) on a scrap, to find what works. See
  [Materials and tests](materials-and-tests.md).
- **Tone test**: grey patches burned with a picture layer's settings and read by the camera, so
  pictures come out with the greys they should. See [Materials and tests](materials-and-tests.md).
- **Trace**: turning a picture into outlines to cut or engrave. See [Pictures](pictures.md).

## U–Z

- **Undo history**: every step you can undo, in a window where you can go back to any of them
  (`Ctrl+H`). See [Undo, redo and history](canvas.md#undo-redo-and-history).
- **Variables**: a serial number, the date or a spreadsheet column written as `{fields}` in text
  and codes, moving on after each finished run. See [Drawing](drawing.md).
- **Weld**: merging overlapping shapes into one outline. See [Editing shapes](editing-shapes.md).
- **Wobble**: the beam traces small overlapping circles as it moves: wider, smoother lines, and a
  wider kerf that lets thick cuts clear their smoke and melt. Works with either laser. See
  [Layer settings](layer-settings.md).
- **Work mode**: flat bed, rotary attachment or conveyor (experimental). See
  [Work area](work-area.md).
- **xf** (job package): the file the machine runs: an archive holding a preview picture, the
  framing G-code, the job's G-code and a description. **Ship It** uploads one; **Export .xf** saves
  one. See [Running a job](running-jobs.md).
