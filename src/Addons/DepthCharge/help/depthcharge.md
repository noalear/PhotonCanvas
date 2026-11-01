# Depthmap with DepthCharge

An add-on (Settings → Add-ons → **Depthmap with DepthCharge**): on when PhotonCanvas is installed, and can be turned off. Its buttons are **Picture → Turn it into → Depthmap**, **Depthmap…** in the Selection panel's Picture settings, and **Depthmap with DepthCharge…** on the right-click menu.

## Depthmap with DepthCharge
<!-- windows: DepthChargeWindow -->

**Depthmap** estimates the depth of the scene in a photo or artwork and makes a depth map: a grey
picture where white is the surface (left alone) and black the deepest part. On a Picture layer in
**Depth slices** mode it's carved as a relief. Select a picture first, or click **Depthmap** with
nothing selected to pick a picture file.

**What it needs.** The depth is worked out by **DepthCharge**, a separate program that is not part of
PhotonCanvas. It must be installed and set up (its own Setup, once). It runs on your own computer,
on the graphics card if it can (DirectML) or the processor (slower); PhotonCanvas talks to it at
127.0.0.1 only and nothing leaves the computer.

- PhotonCanvas looks for it in the folder you chose before, then in
  `%LOCALAPPDATA%\Programs\DepthCharge`, `Documents\DepthCharge` and `OneDrive\Documents\DepthCharge`.
  **Choose DepthCharge folder…** points to another folder (the one with its `app.py` and
  `runtime\python`).
- If DepthCharge is already running it's used. Otherwise it's started in the background when you
  click **Generate depthmap**, and stopped again when PhotonCanvas closes. A DepthCharge you started
  yourself is left running.

**Estimate depth**

| Model | Detail | Notes |
|---|---|---|
| **DA3 Large (best all-round)** | 518, 728 or 1036 px (1036) | Whole-image depth; best for photos and most artwork. |
| **DA3 + fine detail (research)** | 518, 728 or 1036 px (1036) | DA3 for the shape plus Depth Pro's fine detail. Needs DepthCharge's Depth Pro download. |
| **Local close-ups (experimental)** | 518, 728 or 1036 px (518) | Overlapping close-ups for coins and ornament. Slower; can disturb the overall shape. |
| **Depth Pro (research only)** | 1536 px | Apple's model, sharp boundaries. Non-commercial research licence; needs the Depth Pro download. |
| **V2 Small (fast)** | 518 px | A quick baseline. |

Once DepthCharge is running without the Depth Pro download, a chosen **DA3 + fine detail** or
**Depth Pro** goes back to **DA3 Large**. If you generate with one before that, it stops and says
Depth Pro isn't downloaded (DepthCharge's README says how to get it).

**Large pictures.** The model looks at most at **Detail** pixels at once (1036 for DA3), with the
whole picture fitted inside that square. A 3762 × 1254 px picture would then get only 1036 × 345
depth, and every export of it is that blown up. Tiles fix this: the model looks at overlapping
squares of the full-size picture, each filling its whole input, and each is fitted to the whole
picture's depth where it lies. The overall shape stays the whole picture's (tiles can't bend it);
the tiles bring the fine detail.

| Large pictures | What it does | The 3762 × 1254 example |
|---|---|---|
| **Automatic tiles · when needed** (default) | Tiles when the picture is more than 1.25 × **Detail** or further from square than 5:4. Each tile up to twice **Detail**. | 4 tiles, 3108 × 1036 depth, about 1.2 s |
| **Fine tiles · a depth sample per pixel** | Tiles the size of **Detail**: one depth sample per picture pixel. Slower. | 10 tiles, 3762 × 1254 depth, about 2.7 s |
| **Whole picture only** | As before tiles. | 1036 × 345 depth |

The times are with the model already loaded, on the graphics card. The depth is at most 4096 px
on its longest side. Tiles don't apply to **Local close-ups**, which are their own kind of tiling.
They add resolution, not accuracy: the depth is still the model's guess, relative and not in
millimetres. While it runs, the window counts the tiles; when done it says how big the depth is.

**Generate depthmap** runs the model (the first run takes longer while it loads). The window shows
the picture and the depth map side by side.

**Shape the relief.** These sliders reshape the finished depth map without running the model again:

| Setting | Range (default) | What it does |
|---|---|---|
| **Midtone lift** | 0.2 to 3 (1) | Raises or lowers the middle heights (gamma). |
| **Smoothing** | 0 to 5 (0) | Smooths while keeping edges; also smooths the exported map. |
| **Black point** | 0 to 99 (0) | Percent of the range treated as deepest. |
| **White point** | 1 to 100 (100) | Percent of the range treated as shallowest. |
| **Ignore tails %** | 0 to 10 (0) | Ignores this share of extreme depths at each end before scaling; 0.5–2 tames edge spikes. |
| **Tile detail** | 0 to 2 (1) | Tiled pictures only: the fine detail the tiles found. 1 as found, 0 the whole picture's shape alone (as without tiles), 2 doubled. |
| **Direction** | **Emboss · raised relief** | Emboss raises the subject; **Deboss · recessed relief** sinks it. White is always shallow. |
| **Artwork detail** | 0 to 1 (0.25) | Local close-ups only: adds detail from the picture's brightness. Keeps lettering, but shadows and paint can become false depth. |
| **Model detail** | 0 to 2 (0.5) | DA3 + fine detail only: how much of Depth Pro's detail is added. |
| **Detail scale** | 2 to 64 (12) | DA3 + fine detail only: size of the detail added, in pixels at 1024 px. |
| **Image texture** | 0 to 1 (0) | DA3 + fine detail only: adds picture brightness as detail (can turn pigment into false depth). |
| **Remove plain background** | off | DA3 + fine detail only. |

**Open in DepthCharge (crop, mask, 3D)** opens DepthCharge's own editor in your browser: crop
rectangle or circle, masks and a 3D view. Crops and masks saved there are used here after
**Refresh preview**.

**Output.** **Longest edge (px)** (64 to 8192, default 2048) is the size of the map added or saved.
Up to the depth's own size keeps all its detail; bigger only interpolates. After a tiled depth
bigger than the box says, the box is raised to the depth's size (and the window says so). **Save as** (8-bit or 16-bit) applies only to **Save PNG…**; the design
always uses 8 bits.

**Add depthmap to design** puts the map over the original, in the same place and size. A map cropped
in DepthCharge to another shape is fitted inside the original's box, centred. A picture file picked
instead goes in the middle of the work area, up to 100 mm across. It goes on a new
Picture layer, **Relief ·** and the name, set to **Depth slices (relief)** with **Resolution** 254
dpi, **Lines / cm** 200 and **Turn per pass (°)** 37. Set the slices, power and passes there
([Layer settings](layer-settings.md)). The original picture stays on its own layer: turn its output
off if only the relief should run.

Carving depth slices hasn't been tried on the machine yet.

## Tutorials

- [A relief from a depth map](tutorial:pic-depthmap)
