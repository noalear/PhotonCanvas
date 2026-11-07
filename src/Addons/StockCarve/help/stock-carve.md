# Carve stock to a model

An add-on (Settings → Add-ons → **Carve stock to a model**): on when PhotonCanvas is installed, and can be
turned off. It needs **Finish stacks on the rotary**, whose engraving it shares. Its button is
**Laser → Work area → Carve to model**.

You have a rough part in the rotary's chuck (a blank, a glued stack, a piece of branch) and a 3D model
of what it should become. **Carve to model** lines the model up inside the part as it was scanned,
shows in red wherever the model sticks out of the part, and makes a page that engraves the part down
to the model's shape as it turns, moving the focus as the surface comes nearer the axis.

<!-- windows: StockCarveWindow -->

> **Warning:** Not yet tried on the machine. Try a small part in scrap first, and stay by the machine.
> The part must not move in the chuck between the scan and the carving: the engraving goes by where
> the scan found the surface.

## Carve a scanned part to a model

1. Fit the part in the rotary's chuck and scan it (Laser → **Scan the part**, in rotary mode; see
   [Work area](work-area.md#your-own-rotary) for the rotary itself). Leave it in the chuck.
2. Click **Carve to model** on the **Laser** tab. The last scan is the stock. (Or click **Stock
   model…** for a 3D model of the stock, below.)
3. Click **Open a 3D model…** and choose the shape to carve (STL, OBJ or STEP). It's laid with its
   longest side along the axis, in the middle of the stock.
4. Turn, size and move it in **Place it** until it sits where you want it in the stock, or click
   **Best fit**. Red on the model sticks out of the stock: that much can't be carved, as there's no
   material there. The summary says how far it sticks out, or how much room there is to spare.
5. Choose the engrave settings under **Carve**, and click **Put the engraving in the design**. A page
   "… carved on the rotary" is added, with a layer for each focus height.
6. Set Laser → **Work mode** → **Rotary** to the **Object Ø** the summary gives (the job check says so
   if it's different). Turn the part back to where the scan started, focus on its top at the X the
   summary names, then frame and send it.

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
| **Engrave with** | your library's engrave settings with a measured depth (Default engrave settings) | The settings every focus height engraves with. Measure the depth a pass first with the etch rate test (Laser tab). |
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
