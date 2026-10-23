# Precise lines with object snaps
<!-- category: Drawing -->
<!-- summary: Draw lines that start and end exactly on other shapes' corners, centres and crossings, and type their lengths. -->
<!-- page: drawing.md#line -->
<!-- order: 25 -->
Like AutoCAD's object snaps: the ends of a line land exactly on other shapes' points, and you can
type a length. Have a shape or two on the work area to draw from, such as a rectangle and a circle.

## Turn object snaps on
<!-- target: ribbon:View/Object snaps -->
On the **View** tab, check **Object snaps** is on (it is to start with). `F3` turns it off and on.

## Choose the Line tool
<!-- target: ribbon:Home/Line -->
<!-- wait: tool:Line -->
Click **Line** on the **Home** tab (or press `L`).

## Find a point
<!-- target: canvas -->
Move the pointer near a rectangle's corner: a yellow square and "Endpoint" show where a click would
land. Near the middle of a side it's a triangle (Midpoint); on a circle's edge, its centre (Centre).

## Start the line
<!-- target: canvas -->
Click on a corner. The line now follows the pointer, with its length and angle beside it.

## End it on another point
<!-- target: canvas -->
Point at the edge of a circle: the end lands on its centre, or on the point where the line just
touches it (Tangent). Click. The line is drawn and the next one starts from its end.

## Type a length
<!-- target: canvas -->
Point the way the next line should go, type `30` and press `Enter`: a line exactly 30 mm long. Type
`30<90` for 30 mm straight up.

## Stop
<!-- target: canvas -->
Press `Esc` (or `Enter`, or right-click) to stop. The Line tool stays on; `Esc` again goes back to
**Select**.

## Change a line by its grips
<!-- target: canvas -->
Press `Esc` to go back to **Select** and click one of your lines: instead of a box it shows a grip
at each end and one in the middle. Drag an end grip onto another point to move just that end; drag
the middle one to move the line.

## Line up with a point
<!-- target: canvas -->
Rest the pointer on a snap point for half a second: a green cross marks it. Move straight across or
down from it and a pink line shows the pointer lined up with it.

## Choose which snaps
<!-- target: ribbon:View/Snap points… -->
**Snap points…** on the **View** tab turns each kind on or off: **Nearest** (anywhere on a line) is
off to start with.
