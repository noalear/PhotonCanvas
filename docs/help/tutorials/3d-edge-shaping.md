# Shape the edges with engraved steps
<!-- category: 3D and slices -->
<!-- summary: Engrave each slice's top down in steps so the stack follows the model's slopes instead of stair steps. -->
<!-- page: 3d-slices.md#shape-the-edges -->
<!-- order: 30 -->
Stacked slices come out stepped. Edge shaping engraves each slice's top down, a pass a step, where
the model slopes away, so there's far less to sand (not yet tried on the machine). It needs the
depth one engrave pass takes off: run Laser → Etch rate on your material first and save it to the
library.

## Open 3D to slices
<!-- target: ribbon:Home/3D to slices -->
<!-- wait: window:Stacked slices -->
Click **3D to slices** on the **Home** tab.

## Open a model
<!-- target: button:Open a 3D model… -->
Click **Open a 3D model…** and double-click a model. Press **Next** when it shows in the preview.

## Build it as stacked layers
<!-- target: field:Build as -->
Choose **Stacked layers (solid)**. Edge shaping is only for stacked layers.

## Turn on edge shaping
<!-- target: button:Shape the edges: engrave the steps down to the slope -->
Tick it. **Outline** switches to **Outer**, so the slices cover the whole shape and the engraving
takes the rest away.

## Choose the engrave settings
<!-- target: field:Engrave with -->
Choose your etch-rate result for this material. Its depth a pass comes with it.

## Check the depth a pass
<!-- target: field:Depth a pass (mm) -->
This is the height of one step. Leave it as measured; a guess makes the shaping too deep or too
shallow.

## Read what it will engrave
<!-- target: text:Edge shaping: -->
The summary says how many steps on how many parts, how deep, and which parts have slopes underneath
to sand.

## Make the slices
<!-- target: button:Make the slices -->
Click **Make the slices**. The steps go on the **Slices · shaping** layer, engraved after the marks
and before the cut.
