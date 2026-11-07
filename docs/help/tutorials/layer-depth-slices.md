# Carve a relief in depth slices
<!-- category: Layers and settings -->
<!-- summary: Carve a depth map into the material slice by slice, with the focus following it down. -->
<!-- page: layer-settings.md#depth-slices -->
<!-- order: 210 -->
In Depth slices mode a picture is read as a depth map, dark meaning deep, and carved away in thin
slices from the surface down: a relief, on metal with the MOPA or on wood. Not yet tried on the
machine, so start small on scrap. You need a depth map picture (Import → **3D model** makes one
from a model).

## Select the depth map
<!-- target: canvas -->
<!-- wait: picture-selected -->
Click the depth map picture. Its layer must be a Picture layer.

## Depth slices
<!-- target: field:Picture mode -->
Set **Picture mode** to **Depth slices (relief)**. The relief settings appear under **More
settings**.

## How many slices
<!-- target: field:Slices -->
**Slices**: the steps from the surface to the deepest point. More is smoother and longer.

## Line density
<!-- target: field:Lines / cm -->
**Lines / cm**: how densely each slice is scanned. Power, speed, lines and passes decide how deep
each slice cuts.

## Which way is deep
<!-- target: button:Light = deep (negative) -->
Most depth maps are dark = deep. Tick **Light = deep (negative)** if yours is the other way round.

## Follow the floor down
<!-- target: field:Lower focus mm -->
**Lower focus mm** and **every … slices**: lower the focus this much every few slices, about the
depth those slices take off.

## Clean up as you go
<!-- target: field:Cleanup every -->
**Cleanup every**: a gentle pass across the lines after every few slices, to clear debris so the
next slices cut cleanly. Its power, speed and lines appear under it.

## Measure the depth per pass
Materials & Tests → Tests → **Etch rate** (from the Material tests add-on) measures how deep one pass goes on your material, to choose the slices and
the focus drop.
