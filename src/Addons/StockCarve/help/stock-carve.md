# Carve stock to a model

An add-on (Settings → Add-ons → **Carve stock to a model**): on when PhotonCanvas is installed, and can be
turned off. It needs **Finish stacks on the rotary**, whose engraving it shares. Its button is
**Machine → Work area → Carve to model**.

You have a rough part in the rotary's chuck (a blank, a glued stack, a piece of branch) and a 3D model
of what it should become. **Carve to model** lines the model up inside the part as it was scanned,
shows in red wherever the model sticks out of the part, and makes a page that engraves the part down
to the model's shape as it turns, moving the focus as the surface comes nearer the axis.

<!-- windows: StockCarveWindow -->

> **Warning:** Not yet tried on the machine. Try a small part in scrap first, and stay by the machine.
> The part must not move in the chuck between the scan and the carving: the engraving goes by where
> the scan found the surface.

## Carve a scanned part to a model

1. Fit the part in the rotary's chuck and scan it (Machine → **Scan the part**, in rotary mode; see
   [Scan the part](work-area.md#scan-the-part)). For standard stock (a square bar, say) choose its
   shape there, so the stock is its exact shape. Leave it in the chuck.
2. Click **Carve to model** on the **Machine** tab. The last scan's stock model is the stock. (Or click **Stock
   model…** for a 3D model of the stock, below.)
3. Click **Open a 3D model…** and choose the shape to carve (STL, OBJ or STEP). It's laid with its
   longest side along the axis, in the middle of the stock.
4. Turn, size and move it in **Place it** until it sits where you want it in the stock, or click
   **Best fit**. Red on the model sticks out of the stock: that much can't be carved, as there's no
   material there. The summary says how far it sticks out, or how much room there is to spare.
5. Choose the engrave settings under **Carve**, and click **Put the engraving in the design**. A page
   "… carved on the rotary" is added, with a layer for each focus height.
6. The page is made for the **Object Ø** Machine → **Work mode** → **Rotary** is set to (when it's near
   the stock's size), so leave Work mode as it is. If it was set to something far off, or not to the
   rotary, the summary gives the Object Ø to set. If you change Object Ø later, make the page again (or
   set it back): the job check says when they differ, and names the passes that run past once round.
   Turn the part back to where the scan started, focus on its top at the X the summary names, then
   frame and send it.

## Profile cuts: take the bulk off first

Engraving a big part down to a model a tenth of a millimetre a pass takes a long time when most of the
stock has to go. **Profile cuts first…** cuts the waste off in chunks first, the way a bandsaw cuts a
model's front and side profiles out of a block. The part is held still in the rotary, turned to a view,
and the laser cuts straight down through it round the model's outline as seen from above. Everything
outside the outline drops away. Then the part turns to the next view: the model's top and its side (0°
and 90°), then the 45s, then the 22.5s. What's left is the model's shape seen every way round, a little
oversize, and **Carve what's left** hands it back to Carve to model to engrave the rest.

<!-- windows: ProfileCutWindow -->

> **Warning:** Not yet tried on the machine. Before the first cut, check three things with a scrap part:
> **Turn by (°)** +10 in Work mode moves the top of the part toward the back of the machine (if it goes
> toward the front, tick **It turns the other way** for your own rotary, or the cuts come out mirrored); the motor still holds the part during a
> flat job (mark the chuck and the part with a pencil line and look after the first cut); and the waste
> has room to drop without jamming the chuck. Stay by the machine.

1. Place the model in **Carve to model** (or **Best fit** it), then click **Profile cuts first…**. The part
   should be turned to where the scan started (the window starts at 0°; change **Turned (°)** if it isn't).
2. Choose the cut settings under **Cut** and how deep a pass of them cuts.
3. Click **Turn …° to it**. The part turns to the view (the laser stays off). Focus on the top of the part at
   the X the window names, on the laser line.
4. Click **Send the cut**. It goes to the machine as a flat job (Work mode stays on the rotary): the head
   comes down a focus step at a time as the cut goes deeper. Press the machine's start button.
5. Cutting from both sides, the first side goes just past the middle. When its job finishes the window moves
   on to the other side (**This side's done** does that by hand): turn the part half a turn and cut it.
6. When the cut that finishes a view is done, the camera photographs the part (nothing moves, the laser's
   off) and compares it with the photo from just before the cut: where the waste was, the part has dropped
   away if the cut went through. It says **Looks through**, **Partly through** or **Not through yet**.
   Click **More passes** to cut the deepest two focus heights again, or **It's through: next** for the next
   view.
7. After the views (or whenever you like), click **Carve what's left**: Carve to model's stock becomes what's
   left, so its engraving only takes off what the cuts couldn't.

The window remembers where it had got to for the same model in the same place, so you can close it
between cuts.

| Setting | Range (default) | What it does |
|---|---|---|
| **Views** | Top and side / and the 45s / and the 22.5s (all) | How many views round half a turn are cut: 2, 4 or 8. |
| **Cut each view from both sides** | on | Each view is cut from above and, half a turn on, from below, each cut going just past the middle: half as deep as cutting right through. |
| **First view (°)** | −360 to 360 (the model's top) | Which way round the first view looks, from where the scan started. **The model's top** sets it to look down on the model's top as it's placed. |
| **Chuck holds** | Left end / Right end (left) | Which end of the stock is in the chuck. A bar is kept from that end to the model so it stays held; the stock past the model's other end is cut off. |
| **Bar across (mm)** | 1 to 60 (8) | How wide and deep the bar kept from the chuck to the model is. |
| **Keep a bar to the other end too** | off | For a part held at both ends (a tailstock or support). |
| **Cut with** | your library's cut settings (Default cut settings) | The settings every focus height cuts with. |
| **Depth a pass (mm)** | 0.01 to 5 (0.25) | About how deep one pass of them cuts. Each focus height gets enough passes to cut its depth. |
| **Focus step (mm)** | 0.2 to 10 (2) | How far the focus comes down between focus heights. |
| **Cut width (mm)** | 0.1 to 6 (1.2) | Lines side by side round the outline, out into the waste (never into the model), so a deep cut is wide enough for the beam to reach the bottom. |
| **Line gap (mm)** | 0.02 to 1 (0.15) | How far apart those lines are. |
| **Leave on (mm)** | 0 to 5 (0.5) | Left outside the model's outline, for the carving to take off. |
| **Past the middle (mm)** | 0 to 5 (1) | Cutting from both sides: how far past the middle each side's cut goes, so the two meet. |
| **Turned (°)** | (0) | Which way the part is turned now, from where the scan started. The window keeps it up to date as it turns the part; change it if you turned it by hand. |

A deep cut brings the head down a long way (as far as the cut goes below where you focused). The job
check refuses a cut that would bring the head within 3 mm of the part's top, from the focus distance the
machine measured; cutting from both sides halves how far it comes down.

The picture shows the cut from above, the machine's back at the top: grey is where the part is now, green
is what's kept (the model's outline, a little oversize, and the bar to the chuck), and the lines are the
cut, a colour a focus height. Each view's cut only goes where there's still material, and only as deep as
there is material, so later views are quicker. The camera check needs the camera calibrated near the
height of the part's top (Machine → Calibrate camera); without it, look at the part yourself.

## Placing the model

| Setting | Range (default) | What it does |
|---|---|---|
| **Turn X (°)**, **Turn Y (°)**, **Turn Z (°)** | −360 to 360 (as laid) | Turns the model about its own axes, through its middle (X first, then Y, then Z). **Quarter turn** turns it 90° about one of them. The model's X runs along the rotary's axis. |
| **Size (%)** | 1 to 5000 (100) | The model's size; 100 % is as modelled. |
| **Round axis (°)** | −360 to 360 (0) | Turns the model round the rotary's axis, the way a design runs down the page. |
| **Along (mm)** | (the stock's middle) | Where the model's middle is along the axis: the design's X, the same X as the scan. |
| **Off axis, side (mm)**, **Off axis, up (mm)** | −500 to 500 (0) | Moves the model's middle off the axis, sideways or up towards the laser, as the part sits at the start. |
| **Show the stock** | on | The stock as a see-through shell round the model in the 3D view. |

The 3D view lies as the part does in the rotary: the axis runs left to right, and the laser is above.
Drag to turn the view, scroll to zoom.

## Best fit

**Best fit** turns the model round the axis and moves it (along the axis and off it) to where it sticks
out of the stock least, with the room to spare as even as it can be all round. It tries the model
several ways round, so it finds a flat model's place in flat stock. Your own turns (**Turn X**, **Y**,
**Z**) are kept: lay the model the way you want it first.

| Setting | Range (default) | What it does |
|---|---|---|
| **Keep the model's size** | on | Best fit only turns and moves the model. Off: it also makes the model as big as fits in the stock (or shrinks it until it fits). |
| **Room to spare (mm)** | 0 to 10 (0.2) | Best fit tries to keep at least this much stock over the model everywhere, for a scan that's a little out. |

## Carving

The engraving is worked out like [a glued stack's finish](rotary-finish.md#how-the-engraving-is-worked-out):
round the axis is the page's height (the start line half way down), along it is the page's width. At
every spot, the scanned surface comes down to the model a pass at a time, and each pass goes on the
layer for the focus height it's at.

| Setting | Range (default) | What it does |
|---|---|---|
| **Engrave with** | your library's engrave settings with a measured depth (Default engrave settings) | The settings every focus height engraves with. Measure the depth a pass first with the etch rate test (Materials & Tests → Tests). |
| **Depth a pass (mm)** | 0.01 to 2 (0.1, or the chosen settings' depth) | How deep one pass goes on a flat piece with those settings. Nearer the axis the passes go deeper; that's allowed for. |
| **Focus band (mm)** | 0.2 to 5 (1) | How far the surface may be from the focus. Smaller is sharper, with more focus heights. |
| **Deepest (mm)** | 0.2 to 30 (5) | The most taken off anywhere. Spots that need more are left (the summary says how much). |
| **Leave on (mm)** | 0 to 3 (0) | Stops this far short of the model, to sand off. |
| **Core kept (mm)** | 0 to 50 (2) | Never carves closer to the axis than this, so the part stays strong in the chuck. |
| **Detail (mm)** | 0.1 to 2 (0.25) | How finely the surface is mapped. Smaller follows the shape closer, but the page takes longer to make. |
| **Carve past the model's ends too** | off | Off: only the stock between the model's ends is carved, and the rest (with the end in the chuck) is left. On: past the ends it's carved down to the core too. |

## A model of the stock

**Stock model…** takes a 3D model of the stock instead of the last scan: the STL that **Scan the part**
saves, or any model in the same place. Its X runs along the axis (the design's X, mm from the left of
the field), the axis is at Y = Z = 0, and +Z points up at the laser when the job starts. A point θ°
round is at (x, −r sin θ, r cos θ).
