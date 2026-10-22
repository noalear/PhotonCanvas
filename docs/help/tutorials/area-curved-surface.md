# Mark on a curved piece
<!-- category: Work area -->
<!-- summary: Measure a curved piece with the pointer (or type in a cylinder) so the head follows it in height steps. -->
<!-- page: work-area.md#curved-surface -->
<!-- order: 30 -->
On a bowl, a bottle or a slatted barrel, a flat job goes out of focus where the surface falls away.
With the surface measured, the head steps through height bands that follow it. Experimental: try it
on scrap first.

## Before you start
<!-- target: panel:Machine -->
This needs the machine connected and the red pointer calibrated once (the
[Calibrate the red pointer](tutorial:area-calibrate-pointer) tutorial).

## Focus on the piece
<!-- target: panel:Machine -->
Put the piece on the bed and auto-focus on its middle (right end of the ribbon). Heights are
measured from there, up to 8 mm either way.

## Open Curved surface
<!-- target: ribbon:Laser/Curved surface -->
<!-- wait: window:Curved surface -->
Click **Curved surface** on the **Laser** tab.

## Measure, or type it in
<!-- target: field:The surface -->
Keep **Measure it with the pointer**. For a cylinder or drum you've measured with calipers, choose
**Type in a cylinder or drum** instead and fill in its settings.

## Set the area
<!-- target: button:Use the design's area -->
Click **Use the design's area**. **Rows** and **Columns** set how many points: about one every 5 mm
for tight curves.

## Measure
<!-- target: button:Measure -->
Click **Measure**. Each point is read with the pointer and the close-up camera. Red points disagree
with their neighbours: **Measure the red points again**.

## Choose the shape
<!-- target: field:Fit as -->
**Any shape (smooth)**, **Cylinder**, **Drum of flat slats** or **Flat, at a slant**. Drag the 3D
view to check it.

## Turn it on
<!-- target: button:Mark the design on this surface -->
Tick **Mark the design on this surface**. **Height steps (mm)**: 0.2 is what xTool Studio uses.

## Apply
<!-- target: button:Apply -->
<!-- wait: closed:Curved surface -->
Click **Apply**. The surface is kept with the design.

## Test on scrap
<!-- target: ribbon:Laser/Ship It -->
The job bar says the head follows the surface. Ship a small test on scrap first.
