# Engrave fills
<!-- category: Layers and settings -->
<!-- summary: Fill shapes with engraved lines: line spacing, direction, crosshatch, overscan and clean edges. -->
<!-- page: layer-settings.md#engrave-fill-style-and-lines -->
<!-- order: 160 -->
An Engrave layer fills the inside of closed shapes by scanning lines across them. How close the
lines are, which way they run and how their ends are treated decide how even and how dark the fill
comes out. You need a closed shape (draw one with Home → **Rectangle** if needed).

## Make it an Engrave layer
<!-- target: button:Engrave -->
<!-- wait: layer-mode:Fill -->
Select the shape's layer and click **Engrave**.

## Line spacing
<!-- target: field:Lines / cm -->
**Lines / cm**: how close the lines are. More lines are darker, smoother and slower; the line under
it gives the spacing in mm.

## Lines or contours
<!-- target: field:Fill style -->
Under **More settings**, **Fill style**: **Lines** back and forth, or **Offset**, the outline
stepped inward like contour lines (cleaner edges on text and logos).

## Direction
<!-- target: field:Scan angle (°) -->
With **Lines**, **Scan angle (°)** turns the lines: 0 is left to right.

## Crosshatch
<!-- target: button:Crosshatch (second pass at 90°) -->
Tick **Crosshatch (second pass at 90°)** for a second set of lines across the first; **Second pass
at (°)** sets the angle between them.

## Even edges
<!-- target: field:Overscan (mm) -->
**Overscan (mm)**: each line starts and ends this far outside the shape with the laser off, so the
beam is at speed when it reaches the edge.

## Clean outlines
<!-- target: button:Trace the outline after filling -->
Tick **Trace the outline after filling** to trace each shape's outline after its fill: hides ragged
edges.

## Not wider than drawn
<!-- target: field:Dot width (mm) -->
**Dot width (mm)** shortens each line by about the beam's width (0.03–0.08 mm), so fills aren't
wider than drawn.

## Heat-sensitive material
<!-- target: field:Pause per line (ms) -->
**Pause per line (ms)** waits before each line so heat can leave: acrylic, thin or heat-sensitive
material.
