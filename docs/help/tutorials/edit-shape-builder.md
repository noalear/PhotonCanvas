# Weld, subtract, intersect, exclude
<!-- category: Editing shapes -->
<!-- summary: Make one new outline from overlapping shapes: merge them, cut one from another, keep or remove the overlap. -->
<!-- page: editing-shapes.md#weld-subtract-intersect-exclude -->
<!-- order: 30 -->
The shape builder makes one outline out of overlapping shapes: weld letters to a base, cut a notch
out, keep only the overlap. Try all four on the same pair: `Ctrl+Z` brings the two shapes back
each time, still selected.

## Draw two overlapping shapes
<!-- target: canvas -->
<!-- wait: shapes:+2 -->
Draw a rectangle, then a circle that overlaps it.

## Select both
<!-- target: canvas -->
<!-- wait: selection:2 -->
With **Select**, drag a box round them.

## Weld
<!-- target: ribbon:Home/Weld -->
Click **Weld**: one outline round both, without the lines inside. Then press `Ctrl+Z`.

## Subtract
<!-- target: ribbon:Home/Subtract -->
Click **Subtract**: the rectangle (drawn first, so at the bottom) loses what the circle covers.
`Ctrl+Z` again.

## Intersect
<!-- target: ribbon:Home/Intersect -->
Click **Intersect**: only the overlap is left. `Ctrl+Z` again.

## Exclude
<!-- target: ribbon:Home/Exclude -->
Click **Exclude**: everything except the overlap.
