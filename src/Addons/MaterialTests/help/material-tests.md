# Material tests

This add-on adds the Laser tab's **Tests** group: tests that each answer one question about a material (how deep a pass goes, what cuts through, what kerf, how pictures come out) and save the answer with it in the library, plus **Inlay**, which is built on those answers. A burned test opens its results by itself when the job finishes. How the tests work in general, the test grid and Record results are explained in
[Materials and tests](materials-and-tests.md).

| Button | Answers |
|---|---|
| **Etch rate** | How deep one pass goes ([Etch rate](#etch-rate)) |
| **Cut test** | What cuts through, the kerf and the slot fit ([Cut test](#cut-test)) |
| **Exact depth** | Engraving the selection to a measured depth ([Exact depth](#exact-depth)) |
| **Tone test** | Pictures that come out as they look ([Tone test](#tone-test)) |
| **Line test** | Hiding the rows pictures are burned in ([Line test](#line-test)) |
| **Inlay** | A pocket and a piece cut to fit it ([Inlay](#inlay)) |
| **Bevel test** | How far the beam slants at the field's edges ([Bevel test](#bevel-test)) |
| **Graphene test** | Conductive carbon in cork or wood ([Graphene test](#graphene-test-experimental)) |

## Etch rate
<!-- windows: EtchTestWindow -->

How deep one pass of a setting goes. **Etch rate** burns a ladder of small filled circles with the
same setting, the first 1 pass, the next 2, then 3, and so on. The first circle that goes right
through gives the material's thickness ÷ passes. Depths you measure on the shallower ones show how
much it slows down deeper (it usually does: the floor drops out of focus, and smoke and char build
up). The result goes into the library, where **Inlay**, **Exact depth** and stacked slices shaped to
their slopes (3D models and stacked slices) use it.

### Making the etch test

![Etch rate, Make the test tab, with its starting values](images/window-laser-etch-rate--make-the-test.png)

| Setting | Range | Default | What it does |
|---|---|---|---|
| **Laser** | **Diode (blue)**, **MOPA (red)** | Diode | |
| **Power %** | 1–100 | 60 | The setting being measured. |
| **Speed mm/s** | 1–15000 | 200 | |
| **Lines per cm** | 10–1000 | 100 | Fill line density. |
| **Turn per pass °** | 0–90 | 30 | Turns the fill lines each pass, which keeps a deep floor even. Use what you'll use for real. |
| **First circle** | 1–5000 | 1 | Passes on the first circle. |
| **Each next +** | 1–1000 | 1 | Passes added on each next circle. |
| **Circles** | 2–60 | 20 | Five to a row, the pass count marked under each. |
| **Circle Ø (mm)** | 2–20 | 5 | |

The line under them gives the passes from first to last circle, the size, and the time to burn.
**Add the test to the design** puts it in the middle of the bed: each circle on its own layer
(`Etch test · 3 passes`), the numbers on **Etch test labels** (40 % of the power, between 3 and
30 %, at least 300 mm/s).

Run it with the job bar's **Passes** set to **Round by round**: every circle gets its first pass,
then every circle its second, and so on, so each cools between passes, and a circle stops once it
has had all its passes. Put scrap under the material: circles that go through keep burning into what's below.
Stay with it: deep passes can flare up.

### Reading the etch test

![Etch rate, Read the results tab, before a test is in the design](images/window-laser-etch-rate--read-the-results.png)

1. **Material**: pick one, or **New material…** and its name. **Thickness (mm)** (0.1–100) is filled
   from the material; measure it with calipers, since the result is only as good as this number.
2. **Update photo** for thumbnails of the circles.
3. Click the first circle that went right through (hold the board up to the light). It gets a blue
   border, and it and the ones after it a ✓. If none did, click **None went through**.
4. Optionally, type the depth of some shallower circles (depth gauge or the calipers' tail) in the
   box under each.

The result:

- **Through at n passes, not at m**: about *thickness ÷ ((n + m) ÷ 2)* mm per pass, somewhere
  between *thickness ÷ n* and *thickness ÷ m*. If the first circle went through, the rate is at
  least *thickness ÷ n*. This is the average all the way through.
- **None went through**: less than *thickness ÷ most passes*. Try more passes or more power.
- **Depths typed**: the depth after the first pass and the average per pass. With three or more, a
  curve (depth = a·n − b·n²) says how much it slows by the deepest reading; with four or more, how
  closely the readings fit. Getting faster deeper is unusual: often the shallow holes read low
  (char in the bottom, or the gauge not reaching the floor).

The chart draws the hole in cross-section: the surface at the top, depth going down, the bottom of
the material as a line. The burn-through is a straight line from the surface to where it went
through; typed depths are dots with the curve through them.

**Save to the library** adds an engrave recipe `Etch 0.15 mm a pass` with the circles' settings at
one pass, the depth per pass, and what the test found (thickness, where it went through, the
depths) in its notes. The window reads the library back to check the save stuck.

Not yet tried on the machine: whether the through-hole shows clearly enough to pick.

▶ [Measure the etch rate](tutorial:mat-etch-rate)

## Cut test
<!-- windows: CutTestWindow -->

Finds the cut: a grid of small squares at different speeds across and more passes (or power) down.
The squares that drop out, or push out with a finger without tearing, were cut right through, and
the quickest of them is your cut setting. At that setting a second test then measures the kerf and
the slot fit. Both go into the library, where **Inlay**, the **Bevel test** and the stacked-slice
tools (3D models and stacked slices) use them.

### Making the cut test

![Cut test, Make the test tab: the preview of the grid, then speed and rows](images/window-laser-cut-test--make-the-test.png)

The preview shows the grid with every speed over its column and every pass count (or power) beside
its row; darker squares are cut harder, so more likely to go through.

| Setting | Range | Default | What it does |
|---|---|---|---|
| **Laser** | **Diode (blue)**, **MOPA (red)** | Diode | |
| **Slowest (mm/s)**, **Fastest (mm/s)** | 1–15000 | 4, 24 | The speeds across. Slower cuts deeper. |
| **How many speeds** | 1–12 | 6 | |
| **More passes on each row** / **More power on each row** | | Passes | What changes down the side. |
| **Fewest passes**, **Most passes** | 1–100 | 1, 3 | Every whole number between, up to 10 rows. |
| **Power for every square (%)** | 1–100 | 100 | With rows of passes. |
| **Lowest power (%)**, **Highest power (%)** | 1–100 | 60, 100 | With rows of power. |
| **How many** | 1–10 | 5 | Rows of power. |
| **Passes for every square** | 1–50 | 1 | With rows of power. |
| **Square size (mm)** | 4–20 | 8 | Big enough to see whether it dropped, small enough not to tip up into the head once loose. |

The line under them gives the number of squares, the material needed and the cutting time. If the
grid is bigger than the bed, it says so and **Add the test to the design** stays off.

The test goes in the middle of the bed. Each square is its own cut layer
(`Cut test · 100% · 8 mm/s · 2 passes`) with no kerf allowance. Labels go on **Cut test labels**
before every cut layer, marked lightly (12 % of the cut power, between 3 and 15 %, at 200 mm/s;
MOPA 20 % at 1000 mm/s).

> **Safety:** Cut on the honeycomb or pins (with air assist on, if you have it), and stay with it: slow cuts can flare up.

### Reading the cut test

![Cut test, Read the results tab, before a test is in the design](images/window-laser-cut-test--read-the-results.png)

1. **Material** and **Thickness (mm)**, measured with calipers; it's kept with the setting.
2. **Update photo** for thumbnails, laid out as on the bed.
3. Click every square that went through: it gets a green border and a ✓. The squares you click,
   and the setting chosen under **Use**, stay marked when the window opens again (until the program
   closes), as it does after the kerf test.
4. **Use** lists them quickest first (passes ÷ speed, so 2 passes at 12 mm/s beats 3 at 16), and
   picks the quickest. Choose another if its edges were better.
5. **Save to the library** adds a cut recipe `Cuts 3 mm: 8 mm/s × 2`. Its notes list the other
   squares that went through. The material takes the thickness if it had none.

### Kerf and slot-fit comb

**Kerf and fit test at this setting…** adds, at the chosen setting, a 20 mm square and a comb: a
strip with six slots, each the material's thickness plus −0.2, −0.1, 0, +0.1, +0.2 or +0.3 mm,
marked under it. It goes just below the cut grid (above it if there's no room, else the middle of
the bed) on a **Kerf test** layer, labels on **Kerf test labels**. Only one kerf test at a time: an
older one is replaced. The window closes; cut it and open **Cut test** again.

| Field | What to do |
|---|---|
| **Square is (mm)** | Measure across the square with calipers in a couple of places (not corner to corner), and type the average. |
| **Snuggest slot** | Slide the square (it's the same material) into each slot: pick the one it goes into with a firm push and stays. **Not tried yet** leaves it out. |

The kerf is how much smaller than 20 mm the square came out. Set it on the cut layer and parts come
out the size they're drawn. More than 1 mm short gets a warning to measure again. The slot fit is
what the slots of slotted builds (egg crate, radial, boxes) need on top of the thickness: the comb's
fit plus the kerf when the cut layer's kerf is set, the comb's fit alone when it's 0. The window
shows both.

Once the square is measured, **Save to the library** saves the kerf test's setting with the kerf in
its settings and the slot fit beside it.

Not yet tried on the machine: whether a cut square drops cleanly or needs a push, whether the kerf
comes out about 0.1–0.2 mm on 3 mm basswood, and whether the comb's snug slot matches the fit egg
crates need.

▶ [Find the cut](tutorial:mat-cut-test) · ▶ [Measure kerf and slot fit](tutorial:mat-kerf-fit)

## Exact depth
<!-- windows: ExactDepthWindow -->

Engraves the selected shapes to a depth you choose, measuring as it goes. The red pointer, read by
the close-up camera, measures the surface before anything is burned. You burn a round of passes
(shipped as usual), it measures how deep that got, and works out the next round from what each pass
really took off. It closes in on the depth from the shallow side instead of guessing all the passes
up front.
The laser itself never fires while measuring: only the pointer and the head move.

You need: the machine connected, the focus set on the material, and the pointer calibrated once
(**Laser → Curved surface → Calibrate the pointer…**, see
[Work area](work-area.md)). Select closed shapes (not text) and click **Exact depth**.

![Exact depth before starting: the shape with its measuring points, depth, tolerance and first guess](images/window-laser-exact-depth.png)

The picture shows the shapes with the points it measures: blue dots inside (up to three, each at
least 2.5 mm from an edge and as far apart as they can be), grey dots 4 mm outside to the left,
right and top. If the outside points move between readings (the board or the head shifted), that is
taken off. The pointer must see the floor 2.5 mm in from every edge: keep holes at least about
5 mm across.

| Setting | Range | Default | What it does |
|---|---|---|---|
| **Depth (mm)** | 0.05–20 | 1 | The depth to reach. |
| **Within (mm)** | 0.01–0.5 | 0.05 | How close is close enough. |
| **About this deep a pass (mm)** | 0.005–2 | 0.1 | The first guess. Filled in from the library's etch-rate result nearest the shapes' power and speed, if there is one. Only the first round uses it. |

**Start: measure points chosen, a layer of its own** moves the shapes onto a new filled layer
`Exact depth · 1 mm`, copied from their layer, then lists the steps:

1. **Measure the surface**: the pointer reads the height at every dot.
2. **Round 1**: a round aimed at 30 % of the depth with the guess, just to find out how deep a pass
   really goes. **Set up 3 passes and close** (the number varies) sets the layer's passes and closes
   the window. Frame and Ship It.
3. When the job finishes, the window opens again. Brush the dust and char out of the hole, then
   **Measure the depth**.
4. Each next round uses the depth per pass of the last round: 85 % of what's left while that's more
   than four passes, then exactly what's left (1 to 500 passes). A round that took almost nothing
   off (char, a bad reading) can't make the next one huge.
5. When the depth is within the tolerance: **Done**, with how much the points differ. A spread of
   more than twice the tolerance means the floor isn't flat (focus, char or air flow).
   **Finish (keep the shapes on their layer)** ends the run.

The chart **Depth so far** plots the depth after each round against passes, with the depth wanted
as a line. A run in progress is kept with its layer (in the `depth` folder of the program's data
folder), so you can close the program between rounds.

Not yet tried on the machine: how precisely the pointer reads a hole's floor, and whether its dot
shows on charred wood.

▶ [Engrave to an exact depth](tutorial:mat-exact-depth)

## Tone test
<!-- windows: ToneTestWindow -->

Photos that come out as they look. Wood, leather and slate don't darken in step with the grey asked
for: most go dark long before black, and the palest greys barely show, so pictures come out too dark
or flat. The tone test burns a row of grey patches, black to white, with a picture layer's own
settings; the camera reads how light each came out; and that layer's pictures are corrected so their
greys come out as they look on screen.

### Making the tone test

![Tone test, Make the test tab: eleven patches with the sample design's picture settings](images/window-laser-tone-test--make-the-test.png)

| Setting | Range | Default | What it does |
|---|---|---|---|
| **Settings of** | Your picture layers, or **Picture settings for a new layer** | The first | The test burns with exactly that layer's settings (power, speed, resolution, dithering), without any tone correction. |
| **How many** | 5–41 | 11 | Patches from black to white. More read the curve more finely. |
| **Size (mm)** | 3–15 | 6 | Bigger is easier for the camera to read. Half a patch of bare material between patches. |
| **Where the close-up camera sees it (results read from close-up photos)** | | On | Only shown when the close-up camera's position is known at this focus height. Places the patches where it sees, smaller (down to 3 mm) and in rows if needed. |

The line under them gives the size and time to burn, and warns when the layer has more than one
pass: on wood, later passes burn the char of the earlier ones back off, so greys come out lighter and
uneven, not darker. One pass at the power you need reads (and burns) far more evenly.

**Add the test to the design** puts one strip in the middle of the bed (or where the close-up camera
sees) on a copy of the layer named `Tone test · <layer>`. The white patch is never burned: white
isn't visited. Use the material you'll engrave the pictures on, flat and in the camera's view, with
the patches clear of knots and grain lines.

### Reading the tone test

![Tone test, Read the results tab, before a test is burned](images/window-laser-tone-test--read-the-results.png)

1. **Update photo** (the camera must see the whole strip), or **Close-up photo**. The patches are
   found wherever you moved the strip to before burning. Use the **Lights** if glare or a dim photo
   spoils it.
2. Each patch shows its grey and how light it read. The bare material is read from a band 1 to
   2.5 mm above the strip.
3. **How this material takes greys**: grey sent along the bottom, how light it came out up the side.
   The dashed diagonal is what you'd want; the further the dots are from it, the more pictures
   change when corrected.
4. The summary under it says whether mid-grey comes out too dark (under 42 % light), too pale (over
   58 %) or about right, from which grey down the material is as dark as it goes (darker greys only
   add heat), which pale greys barely show, and whether there's a jump from bare wood to the first
   burn (then lower **Lightest power %** or speed up, and test again).
5. **See it before you burn**: pick a **Picture** from the design, or **A grey gradient**, and see
   **Your picture**, **Burned as it is** and **Burned corrected**, in the material's own colours
   (the bare colour and the darkest burn, read off the photo).
6. **Use it**: pick the **Layer** and click **Correct this layer's pictures**. The correction holds
   only while that layer's settings stay as tested.
7. **Save to the library**: a picture recipe `Pictures, matched (…)` on the chosen **Material**,
   with the settings and the correction.

A reading that can't be right is shown but not used: when the white patch, which isn't burned, reads
under 80 % as light as the bare material, or a lighter grey reads clearly darker than a darker one.
Then the camera was reading the wrong spots: check the photo lines up with the strip (calibrate the
camera if not), nothing shades it, and it's on clean material.

### How pictures get corrected

The test gives the material's response: for each grey sent (black to white), how light it came out,
from as dark as these settings go to the bare material. Readings are smoothed so a lighter grey never
comes out darker. Turned round, it says which grey to send for each tone wanted. When a job is made,
every pixel of a picture on that layer (after the picture's own tone adjustments) is sent as the
grey that gives its tone, before dithering or greyscale power. In greyscale, power still runs from
**Lightest power %** for the palest burned tone to the layer's power for black.

The correction travels with the settings: it's saved with the layer in the design, kept in picture
recipes, and comes back when you apply one. Picking a material for a picture layer in
[Layer settings](layer-settings.md) also brings in that material's tested correction nearest the
layer's power, speed and resolution. If a line test changes the layer's resolution, spreading or
direction, it reminds you to run the tone test again.

### Pictures as burned

**View → Pictures as burned** draws pictures on layers that have a correction as they'll come out:
corrected, in the material's own colours. Other pictures show as they are.

On basswood in October 2026, a strip burned at 30 % × 3 passes, turned 25° each pass, came out
lighter and uneven: the later passes burned the char of the earlier ones back off. Still to check:
how cleanly the camera reads a burned strip (light, glare, grain), and a portrait burned with and
without the correction.

▶ [Match pictures to the material](tutorial:mat-tone-test)

## Line test
<!-- windows: LineTestWindow -->

Pictures are burned in rows, and sometimes the rows show. The line test burns the same patch with a
picture layer's settings, then again with one thing changed each time, side by side, so you can see
which change hides them. Rows often show only where the picture changes from dot to dot (burning back
and forth, a late power change lands on opposite sides in neighbouring rows), which flat grey can't
show, so the patch can be a piece of your own picture.

### Making the line test

![Line test, Make the test tab: a piece of the sample picture and the seven patches](images/window-laser-line-test--make-the-test.png)

- **Settings of**: the picture layer to try (relief layers aren't offered). Every patch starts from
  exactly its settings.
- **Patch shows**: **A piece of** each picture on that layer, or **Grey bands and edges**: 25, 50 and
  75 % grey side by side on top; below, 0.5 mm bars, a ring on mid grey and a ramp from black to
  white.
- The piece picker shows the picture with a square: the piece every patch shows, first put on the
  spot with the widest spread of light to dark, with both smooth shading and fine detail. Click the
  picture to move it. The piece is the patch size at the size the picture is placed.
- **Patch size**: **Size (mm)** 6–25, default 10. Bigger is easier to judge by eye.
- **Where the close-up camera sees it (results from close-up photos)**: as in the tone test.

**The patches, left to right**, each changing one thing from the first:

| Patch | What changes |
|---|---|
| **As set** | Nothing. |
| **Spread out** / **Not spread** | The fill lines burned every few lines first, then the ones between, or in order. |
| **Scattered** | Only when the layer spreads in a set pattern: spread in no set order, so no row pattern repeats. |
| **One way** / **Both ways** | Every row left to right (slower), or back and forth (faster). |
| Two finer resolutions | 4/3 and 5/3 of the layer's dpi (rounded to 10, at most 2540), with the power and **Lightest power %** lowered by the same share, so they put in the same energy. |
| **2 passes turned**, **3 passes turned** | Two and three times the passes, each turned 180° ÷ passes from the last (90°, or 60°), the power divided by √2 and √3: a little more energy in all, because weak passes mark less. |

**Add the test to the design** replaces any earlier line test and puts the patches in the middle of
the bed (or where the close-up camera sees), each on its own layer (`Line test · One way`).

### Comparing the patches

![Line test, Compare tab, before a test is in the design](images/window-laser-line-test--compare.png)

Look at the burned piece itself, close up, in light from the side: single rows are finer than the
camera can see. **Update photo** or **Close-up photo** put a photo on each card so you know which is
which; the **Lights** are there for glare. A card says **Not in the photo** when its patch isn't in
it.

Pick the **Layer** under **Use it** and click **Use these settings** on the best card: the layer
takes that patch's resolution, power, passes, **Lightest power %**, spreading, direction and line
angles. Everything else stays. To fine-tune, make the test again from the new settings: each round
changes one thing from where you are. **Remove the test from the design** deletes the patches and
their layers.

### Line test findings on basswood

From line tests on 2.75 mm basswood in October 2026 (greyscale, 400 dpi at 60 %, then 400 dpi at
40 % × 3):

- Spreading the lines left woven or brick-like rows, both one way and both ways. Not spread left no
  rows, but came out too dark and blotchy. Scan direction made no difference.
- Finer lines at proportionally lower power (530 dpi at 45.3 %, 670 dpi at 35.8 %) came out lighter,
  with a much better gradient: wood marks far less at low power. 530 dpi looked best; at 670 dpi rows
  came back in the deep areas.
- Weak passes didn't add up: the same energy split into 6 passes at 20 % barely marked, and 9 at
  13.3 % not at all. That's why the turned patches lower the power only by √2 and √3.

▶ [Hide the rows in pictures](tutorial:mat-line-test)

## Inlay
<!-- windows: InlayWindow -->

Makes an inlay from the selected closed shapes: a pocket engraved into the board as deep as the
veneer, and the piece cut from the veneer to fit, so it sits flush. Select the shapes, then
**Inlay** (it's off until a shape is selected).

![Inlay, with an example cut recipe chosen](images/window-laser-inlay.png)

| Setting | Range | Default | What it does |
|---|---|---|---|
| **Veneer (mm)** | 0.1–10 | 0.6 | The thickness of what goes in: the pocket goes this deep. |
| **Gap each side (mm)** | 0–1 | 0.05 | Room round the piece for glue and the wood moving. 0.03–0.08 mm is snug. |
| **Engrave with** | **Default engrave settings**, or a recipe with an etch rate | The first etch recipe | The pocket's settings. |
| **Depth a pass (mm)** | 0.01–2 | 0.15 | Filled in from the etch recipe. Without one it's a guess, and the piece may sit proud or sunk. |
| **Cut with** | **Default cut settings**, or a cut recipe | The first cut recipe | The veneer's settings. |
| **Kerf (mm)** | 0–1 | 0.1 | Filled in from the cut recipe, so the piece comes out the drawn size. |

The line under them says how many passes the pocket takes and how deep that comes out.

**Make the pocket and the piece** adds:

- On this page, on the board: an **Inlay · pocket** layer (filled) with the shapes joined and grown by
  the gap. Its passes are the veneer's thickness ÷ depth a pass, rounded up, each turned by the
  recipe's turn per pass, or 37° if it has none, so the floor comes out flat.
- On a new page **Inlay piece**, in the same place: an **Inlay · piece** cut layer with the shapes at
  their drawn size and the kerf set.

Your original shapes stay as they were. Burn the pocket, put the veneer in, switch to the **Inlay
piece** page and cut. Not yet tried on the machine.

▶ [Make an inlay](tutorial:mat-inlay)

## Bevel test
<!-- windows: BevelTestWindow -->

Away from the middle of the field, the galvo's beam comes down at a slant, so cut edges there slope
instead of standing upright. Edge by edge, that slope could make bevels (pyramids, chamfered boxes,
sloped stacked slices), once it's known how much the beam slants. This test measures it: four bars
are cut through a board, and their lengths at the bottom and the holes' lengths at the top give the
height of the mirrors' pivot above the material. Bevel cutting itself isn't built yet; this is the
first step.

![Bevel test, Make the test tab, with a cut layer of the sample design chosen](images/window-laser-bevel-test--make-the-test.png)

**Cut with** lists the cut layers in the design and the cut recipes in the library: use settings that
cut right through your board cleanly, so the bars drop out. **Add the test to the design** adds a
**Bevel test** layer with four bars 12 mm wide, 150 and 40 mm long, two across and two down, in an L
along the bottom and right of the bed. Use a board at least 4 mm thick: the slope is a fraction of a
millimetre per millimetre of thickness. Let the bars drop out and leave the board where it is until
you've measured the holes.

![Bevel test, Measurements tab](images/window-laser-bevel-test--measurements.png)

On **Measurements**, in millimetres: **Board thickness** (filled from a recipe's material if empty),
then for each bar its length with the calipers' outside jaws (they close on its bottom face, the
widest) and the length of its hole with the inside jaws from the top of the board (the top opening,
the narrowest). Clear the ash off first; two decimals if your calipers show them. Commas work as
decimal points.

From the long and short bar of one direction: pivot height = thickness × (150 − 40) ÷ ((long bar −
long hole) − (short bar − short hole)). The kerf is the same for both bars, so it cancels out; a
difference under 0.02 mm is too small to use. For each direction the window gives the pivot height,
the slope an edge at the side of the field gets, and how far from the middle 5° and 10° are. It warns
when the two heights are more than a quarter apart or under 80 mm: measure again, jaws square on the
edges. **Keep these for bevel cuts** saves them (one direction measured is used for both).

Not yet tried on the machine. If the pivot is about 300 mm up, the steepest bevel is about 19°; at
600 mm only about 10°.

▶ [Measure the beam's slant](tutorial:mat-bevel-test)

## Graphene test (experimental)
<!-- windows: GrapheneTestWindow -->

Burning cork, wood or polyimide (Kapton) can turn its surface into conductive carbon, laser-induced
graphene, for touch pads and simple sensors. Whether the blue diode does it on your material is what
this finds out: dumbbell-shaped burns (two 5 mm contact pads and a strip between) at a grid of
powers and speeds, which you measure pad to pad with a multimeter. Not yet tried on the machine.

![Graphene test, Make the test tab, with its starting values](images/window-laser-graphene-test--make-the-test.png)

| Setting | Default | What it does |
|---|---|---|
| **Powers (%)** | 30, 45, 60, 75 | Across the grid, separated by commas. |
| **Speeds (mm/s)** | 50, 100, 200 | Down the grid. |
| **Focus raised (mm)** | 1.5 (0–10) | Graphene usually wants a wider, softer spot: the head goes this much higher for the strips. |
| **Strip length (mm)** | 15 (5–50) | |
| **Strip width (mm)** | 3 (1–10) | |

**Add the test to the design** puts the grid near the top-left of the bed: each strip its own
filled layer (`Graphene · 45% 100 mm/s`, blue diode, 150 lines per cm), numbered, with the powers
along the top and speeds down the side on **Graphene · labels**. The burn should be black and
velvety, not grey ash: too much power ashes it, too little leaves brown. Cork and polyimide work best
in published papers; wood needs more trial. Ventilate well.

The window doesn't open by itself after the job: open **Graphene test** again and it starts on
**Readings**. Set the multimeter to resistance (Ω), press one probe on each pad of a strip and type
what it reads (`1500`, `1.5k` and `2M` all work). Leave it empty if it reads nothing. Readings are
kept between sessions. The lowest is the best, with its resistance per square (resistance × width ÷
length): under about 100 Ω per square is good laser graphene; thousands is barely conductive.
**Save the best to the library** adds an engrave recipe `Graphene (… per square)`; a new material
goes under *Cork & rubber*.

▶ [Try laser-induced graphene](tutorial:mat-graphene-test)

## Tutorials

- [Measure the etch rate](tutorial:mat-etch-rate)
- [Find the cut](tutorial:mat-cut-test)
- [Measure kerf and slot fit](tutorial:mat-kerf-fit)
- [Engrave to an exact depth](tutorial:mat-exact-depth)
- [Match pictures to the material](tutorial:mat-tone-test)
- [Hide the rows in pictures](tutorial:mat-line-test)
- [Make an inlay](tutorial:mat-inlay)
- [Measure the beam's slant](tutorial:mat-bevel-test)
- [Try laser-induced graphene](tutorial:mat-graphene-test)
