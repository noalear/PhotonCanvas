# Remove doubled lines
<!-- category: Editing shapes -->
<!-- summary: Cut shared edges once by removing lines that lie on top of each other. -->
<!-- page: editing-shapes.md#remove-overlapping-lines -->
<!-- order: 150 -->
Parts drawn side by side share edges, and the laser would cut each shared edge twice. This removes
the second copy of every doubled stretch.

## Select the parts
<!-- target: canvas -->
Select the parts that touch, or nothing to work on the whole design.

## Remove the doubles
<!-- target: ribbon:Home/More… -->
Click **More…** and choose **Remove overlapping lines**. The status bar says how many mm were
removed.

## Or keep the drawing as it is
<!-- target: panel:Layer settings -->
Outlines that lost an edge are now open lines. To keep them closed, undo and tick the Cut layer's
**Cut shared edges once** instead: it does the same when the job is built.
