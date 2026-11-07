# Work area: rotary, curved, layer by layer, sections, pieces

The **Work area** group on the **Laser** tab changes where and how a job is marked: on a rotary
attachment or the conveyor instead of the flat bed, on a curved piece, one layer at a time, one
section at a time for a design bigger than the bed, or across several pieces that go together.

| Button | What it's for |
|---|---|
| **Work mode** | Flat bed, rotary attachment or conveyor, and their settings. Hidden unless you have an attachment. |
| **Curved surface** | Mark on a curved piece: the head follows it in height steps. |
| **Layer by layer** | Mark one layer at a time, with a stop between to change the screen, colour or material. |
| **Sections** | Mark a design bigger than the work area, moving the material between sections. |
| **Split onto pieces** | Mark one design across several pieces (four boards making one picture): a page per piece, lined up by the camera. From the Split onto pieces add-on, which has its own page under **Add-ons**. |

In a narrow window, the small buttons show only their icons: hover over one for its name.

![The Laser tab, with the Work area group near the right (Work mode is hidden here: no attachment is ticked)](images/ribbon-laser.png)

## Work mode

<!-- windows: WorkModeWindow -->

**Work mode** chooses what the job is marked on: the **Flat bed** (the usual 220 × 220 mm), a
**Rotary** attachment (round things: cups, tumblers, rings) or the **Conveyor** feeder (long pieces,
up to 610 mm).

> **Note:** The **Work mode** button is hidden until you tick an attachment in Settings →
> **Equipment** (**RA2 Pro rotary (rollers and chuck)**, **RA3 rotary (rollers and chuck)**,
> **My own rotary (homemade or another make)** or **Conveyor feeder**). You're asked this once when the program first starts. Only the attachments
> you tick are offered. Untick the one in use and jobs go back to the flat bed.

> **Warning:** The rotary and conveyor modes are experimental. Their commands follow xTool Studio's
> own (the mechanical U axis), but they haven't been run on the machine yet. Fit the attachment as
> xTool describes and try a small mark on scrap first. Every job made for them says so in the job
> bar.

![The Work mode window (flat bed only: no attachment ticked)](images/window-laser-work-mode.png)

Choose the mode at the top, set its settings, and click **OK**. The work area on the canvas changes
to the mode's size and the view fits it; the status bar says the mode. **Cancel** changes nothing.

### Rotary

| Setting | Range | Default | What it does |
|---|---|---|---|
| **Attachment** | the ones you have: RA2 Pro · rollers, RA2 Pro · chuck, RA3 · rollers, RA3 · chuck, Your own rotary | the first | Which attachment and how it holds the object. |
| **Object Ø (mm)** | 2 to 200 | 60 | The object's diameter where it's marked. With rollers it sets how far round the design goes; with the chuck it also sets the scale. |
| **Laser line (mm)** | 1 to 219 | 110 | Where the laser draws across the object, measured from the back edge of the 220 mm field (110 is the middle). Put it over the top of the object, along the rotary's axis. **Frame** a job to see the line, and change the number until the line sits there. |

- The work area becomes the field's width (220 mm) × once round the object. The window shows
  "Once round": π × the diameter. Only the first 375 mm can be used (xTool Studio's limit).
- Design X runs along the object (the galvo), design Y goes round it (the attachment turns it). The
  middle row of the design faces the laser at the start, unless the page has a **rotary start
  line**: an orange guide across the page labelled "Rotary start: faces the laser". Then that line
  faces the laser at the start, whatever is drawn. Pages made for the rotary (a stacked model's
  finishing page) have one; drag it like any guide, or drop it on the ruler to remove it.
- On the rotary a layer's **Focus offset (mm)** can go up to 60 mm either way (10 mm on the flat
  bed), for surfaces much nearer the middle than where you focused.

#### Turning the rotary

The **Turn the rotary** box in the rotary settings turns the object straight away, with the laser
off. The rotary's motor is set to hold first, then turned, and it keeps holding the object still
afterwards. Keep hands clear of the chuck. If the machine won't turn it that way (older firmware),
the turn goes as a short laser-off job instead, which starts as soon as it arrives, after you confirm.

| Setting | Range | Default | What it does |
|---|---|---|---|
| **Turn by (°)** | −3600 to 3600 | 90 | How far to turn the object, in degrees. Positive turns it the way a design runs down the page; negative the other way. Click **Turn**. It saves the settings above first. |
| **It made (turns)** | 0.05 to 20 | 1 | For the one-turn check: how many turns the object really made when you clicked **Turn once**. |

- **Turn once** turns the object one whole turn. Put a tape mark on the chuck first. If it doesn't come
  back to the mark, type how many turns it made under **It made (turns)** (a quarter past is 1.25, a
  quarter short 0.75) and click **Correct**. Turning outside a job uses the machine's conveyor feed,
  which has its own scale, so this check matters even when jobs already turn the right amount.
- **Stop turning** stops a turn part way.
- **Let the motor go** lets the motor go limp, so you can turn the object by hand.

The rotary has no home: each job starts wherever the object is, and turns it back to there at the
end. A job that is stopped part way (Stop, or a long press on the machine's button) skips that, so
the object is left turned and the next job starts in the wrong place. When a rotary job sent from
PhotonCanvas is stopped, the program works out how far the object had turned. It uses the
machine's own progress when it reports one, and the time otherwise. It then shows a **Rotary 85°**
chip (with the angle) in the job bar, and **Return rotary to start** in the Turn the rotary box.
Click it to turn the object back, or click **It's already back** if you turned it by hand. The angle
is worked out, not measured, so it can be a little out. A pencil line across the chuck and the
object shows what's left, and **Turn by (°)** takes up the rest. If you stop again before turning
back, the turns add up. The program only knows while it's open and connected.

#### Your own rotary

**Your own rotary** is a rotary you made (or another make) plugged into the machine's rotary port.
Tick **My own rotary (homemade or another make)** in Settings → **Equipment** first. It has its own
settings:

| Setting | Range | Default | What it does |
|---|---|---|---|
| **Holds the object** | Chuck, Rollers | Chuck | A chuck turns the object with the motor; rollers turn it by its surface. |
| **Units a turn** | 1 to 100000 | 128 | Chuck: the machine's units for one whole turn of the object. xTool's chucks take 128; other gearing or motor steps need their own number. |
| **Roller scale** | 0.001 to 1000 | 0.59917 | Rollers: the machine's scale for a millimetre of surface (the RA2 Pro's). |
| **It turns the other way** | off | off | Tick if a test mark comes out mirrored round the object (the motor turns the other way from xTool's). |

To check the scale, make a design as tall as the work area (once round), mark a line across it, and
put a tape flag on the object. Run it on scrap and count how far the object turned while it drew the
line. Only count that slow turn: the quick turns before and after just go to the start and back. If it isn't one
whole turn, type 1 under **Turns asked** and what it made under **Turns made** (a quarter is 0.25),
and click **Correct the scale**: the number is multiplied by asked ÷ made. Click **OK** to keep it,
and run the test again to check.

> **Warning:** The machine drives your rotary through its own port as if it were xTool's. Check
> the motor and wiring suit the port before you plug it in, and keep a hand near the stop button on
> the first test.
- With rollers the scale round the object is fixed (the RA3's is four times the RA2 Pro's). With
  the chuck it's worked out from the diameter, so the diameter must be right for the design to come
  out the right size.

### Conveyor

| Setting | Range | Default | What it does |
|---|---|---|---|
| **Belt calibration %** | 90 to 105 | 100 | The belt's calibration from xTool Studio's conveyor calibration. |
| **Angle** | −10 to 10 | 0 | The belt's angle compensation from the same calibration (0 if never calibrated). |

**Read from the machine** fills both in from the calibration stored on the machine (needs the
connection). The work area becomes 610 × 220 mm: design X moves the belt, and the laser draws across
it.

### What changes on an attachment

- Fill lines on the conveyor always run across the belt (90°): scan angles and turning per pass
  are left out.
- A picture layer's scan angle, crosshatch and turning per pass only work on the flat bed: on an
  attachment its pictures are burned in straight rows. The job bar warns.
- **Frame** only traces the galvo's own direction: a thin box across the middle of the field (the
  object or belt doesn't move while framing).
- **Curved surface** is left out, and **Sections** is greyed out.

## Curved surface

<!-- windows: CurvedSurfaceWindow -->

**Curved surface** marks a design on a piece that isn't flat: a bowl, a bottle lying on its side, a
barrel of slats, a board that sits at a slant. You give PhotonCanvas the shape (measured with the red
pointer and the close-up camera, or typed in), and every job is then marked in height bands that
follow it. It works on the flat bed only.

> **Warning:** Experimental, not yet tried on the machine: calibrating the pointer, measuring a
> real piece, and a curved job being accepted by the machine are all still to be checked. Try it on
> scrap first. Every curved job says so in the job bar.

![The Curved surface window](images/window-laser-curved-surface.png)

### How a curved job is marked

The head doesn't follow the surface continuously: as xTool Studio does, it steps through height
bands, highest first (0.2 mm apart unless you change **Height steps**). In each band it marks just
the parts of the design whose surface lies in that band: lines are cut where they cross into the next
band, fill lines likewise, and pictures are split into one picture per band. The head's height for
each band is the surface's height there, less the layer's own focus offset, so focus offsets still
work.

In the job:

- Parts of the design outside the measured area get guessed heights: the job bar warns. Measure an
  area that covers the whole design.
- A Cut or Score layer's **Step down/pass** is left out: the head's height follows the surface
  instead. An Engrave layer's still lowers each pass further below the surface.
- A picture layer's scan angle, crosshatch and turning per pass are left out (straight rows).
- Fills leave out the pause before each line, overscan and turning per pass; the job bar says so.
- With fewer than 3 measured heights (and no typed shape), the design is marked flat; the job bar
  says so.
- With a rotary or conveyor work mode, the curved surface is left out.

### The window

The left side is a 3D view of the surface: a mesh over the area, the measured points (white; red
when they disagree with their neighbours) and your design's outline lying on it. Drag to turn it, scroll to zoom, middle-drag or the arrow keys to move
it, and press **F** to see it all again.
Heights are drawn taller than life so small curves show; the corner says by how much, and the range
of heights.

- **Mark the design on this surface**: tick it to have jobs follow the surface. Untick it to keep
  the measurements but mark flat.
- **The surface**: **Measure it with the pointer**, or **Type in a cylinder or drum**.
- **Height steps (mm)** (0.05 to 1, default 0.2): how far the head moves between bands. Smaller
  follows the surface more closely and takes longer.
- **Apply** keeps the surface with the design (saved with it; `Ctrl+Z` undoes it). **Cancel**
  closes without changing anything: points measured in the window are only kept by **Apply** (a
  pointer calibration is kept either way).

While the window is open the rest of the program waits, so focus on the piece before opening it.

### Calibrate the pointer

Measuring uses the red pointer and the close-up camera: the pointer comes in at a slant, so its dot
lands in a different place in the close-up picture as the surface rises or falls. Calibrating once
records that. It's kept for good, and **Focus on selection** on the Camera tab uses it too.

1. Put a flat, matt board under the head, covering the middle of the bed. Type its thickness in
   **Board (mm)** (0.5 to 150; it starts at the current focus distance).
2. Click **Calibrate the pointer…** and **Start**. The head moves up and down, 8 mm either side of
   its focus on the board, in nine steps of 2 mm. At each step the pointer is aimed at 25 points over
   the middle 30 × 30 mm of the bed and photographed. The laser isn't fired. About two minutes.

It fails if the dot was seen in fewer than a third of the photos: check the close-up camera sees the
area, and that the board isn't shiny. The line above **Board (mm)** says whether the pointer is
calibrated.

### Measure it with the pointer

Focus on the middle of the piece first (auto-focus, or type its height at the right end of the
ribbon): heights are measured from the focus height, up to 8 mm above or below it.

| Setting | Range | Default | What it does |
|---|---|---|---|
| **Area X**, **Area Y** | 0 to 220 mm | see below | The top-left corner of the area to measure. |
| **Width**, **Height** | 2 to 220 mm | see below | Its size. It must be where the close-up camera can see the dot. |
| **Rows**, **Columns** | 3 to 40 | 9 × 9 | How many points. More follow the shape better: one every 5 mm or so for slats or tight curves. |

The area starts as the one measured last, else the design's area plus 3 mm, else a 50 mm square in the
middle of the bed. **Use the design's area** sets it to the selection's area (or the whole design's)
plus 3 mm round it.

- **Measure** checks the machine is idle, moves the head to the focus height, turns the fill lights
  off, and for each point aims the pointer and photographs it with the close-up camera (each photo is
  compared with one with the pointer off, so only the dot is left). The lights and pointer are put
  back afterwards. The status line says how many points were read: a point can't be read when the dot
  isn't seen, or the surface there is more than 8 mm from the focus.
- **Stop** stops measuring.
- Points more than 0.6 mm from the smooth surface through the other points are shown red and left
  out of the fit. **Measure the red points again** measures just those.

Then choose how the points become a surface:

| **Fit as** | For | Notes |
|---|---|---|
| **Any shape (smooth)** | Bowls, waves, anything | A smooth sheet through or near every point. **Smoothing** (0 to 1, default 0.3): 0 goes through every point exactly, 1 is very smooth and rides over bad points. |
| **Cylinder** | A round object lying on its side | The best cylinder through the points (6 points at least); its diameter is shown. |
| **Drum of flat slats** | A barrel or slatted tube | A many-sided drum with flat faces. **Slats round it** (3 to 200, default 12): the faces round the whole object. The size across the flats is shown. |
| **Flat, at a slant** | A flat piece that doesn't lie level | A tilted plane. |

The line underneath says how many points there are and how closely they sit on the surface.

### Type in a cylinder or drum

For a round object lying on its side. Measure it with calipers: that's far more exact than the
pointer.

| Setting | Range | Default | What it does |
|---|---|---|---|
| **Shape** | Cylinder, Drum of flat slats | Cylinder | Round, or made of flat slats. |
| **Slats round it** | 3 to 200 | 12 | Drum only: the faces round the whole object. |
| **Axis runs** | Front to back (along Y), Left to right (along X) | Front to back | Which way the object lies on the bed. |
| **Axis X**, **Axis Y** | −100 to 320 mm | 110, 110 | A point the axis passes through, seen from above. **Axis through the design's middle** puts it there. |
| **Top height** | 1 to 150 mm | 50 | The height of the top above the bed, as you'd focus on it. |
| **Diameter** | 2 to 400 mm | 80 | For slats: across the flats. |

## Layer by layer

<!-- windows: LayerByLayerWindow -->

**Layer by layer** marks one layer at a time, with a stop between layers to change something: the
screen and ink colour when making screen-printing screens, or the material or its colour. Each
layer is its own job, sent with **Ship It** as usual.

![The Layer by layer window](images/window-laser-layer-by-layer.png)

The window lists the layers that would be marked (output layers with shapes on them), numbered in
order. The first one is picked when it opens.

1. Click a layer: from now on the job bar builds a job of only that layer (and its sub-layers).
2. **Ship It** and press the machine's start button.
3. Change the screen, colour or material, then click the next layer and ship again.
4. **Done** closes the window, and jobs mark the whole design again.

The window stays open beside the canvas while you work. For screen printing, flip the design first
(Arrange tab → **Flip across**; see [Arranging](arrange.md)); **CMYK split** on the Picture tab makes
one picture per ink, each on its own layer (see [Pictures](pictures.md)).

> **Note:** Serial numbers and spreadsheet rows move on once, after the last layer's job finishes
> (not after each layer), so every layer of a piece marks the same number.

## Sections

<!-- windows: SectionsWindow -->

**Sections** marks a design bigger than the 220 × 220 mm work area, a section at a time: the material
is moved by hand between sections, against a fence or stop, so the pieces meet. Greyed out unless the
work mode is the flat bed. Not yet burned on the machine (it's built from commands already checked).

![The Mark in sections window](images/window-laser-sections.png)

**Mark in sections** says how big the design is (everything on output layers) and how many sections
that makes.

| Setting | Range | Default | What it does |
|---|---|---|---|
| **Section width (mm)** | 10 to 220 | 200 | The largest width of one section. |
| **Section height (mm)** | 10 to 220 | 200 | The largest height of one section. |

The design is cut into a grid of sections that size, numbered row by row from the top left. Each
section is marked in the same place: the middle of the work area. Under each section the list says
how to move the material for it, from where it was for section 1 ("Material: as it is" for the first;
then, say, "move it 200 mm left from where it was for section 1").

1. Set the material against a fence or stop, placed for section 1.
2. Click **Section 1 · row 1, column 1**: the canvas outlines that part of the design ("Marking
   this section") and the job bar builds a job of only that part. **Frame** traces where it will
   land.
3. **Ship It** and press the machine's start button.
4. Slide the material by the amount shown for the next section, click it, and ship again. The camera
   picture shows whether it lines up.
5. **Done** closes the window, and jobs mark the whole design again.

How the sections are cut: lines are cut at the section's edge; fills keep their whole outline and are
clipped line by line, so the lines meet across sections; pictures are clipped dot by dot. Serial
numbers and spreadsheet rows only move on after the last section.

## Try it

- [Mark on a rotary or the conveyor](tutorial:area-work-mode)
- [Calibrate the red pointer](tutorial:area-calibrate-pointer)
- [Mark on a curved piece](tutorial:area-curved-surface)
- [Mark one layer at a time](tutorial:area-layer-by-layer)
- [Mark a design bigger than the bed](tutorial:area-sections)
