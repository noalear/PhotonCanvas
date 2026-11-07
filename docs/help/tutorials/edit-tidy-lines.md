# Tidy an imported drawing
<!-- category: Editing shapes -->
<!-- summary: Join and close the loose lines of an imported DXF or other drawing. -->
<!-- page: editing-shapes.md#tidy-lines -->
<!-- order: 110 -->
Drawings from CAD programs often arrive as separate lines and arcs that only touch: shapes that
look closed but aren't, so they can't be engraved and get no kerf or tabs. Tidy lines joins them
up.

## Import the drawing
<!-- target: ribbon:Import/Import -->
Bring in a DXF or other drawing with **Import** (`Ctrl+I`), or use one you have.

## Select it, or nothing
<!-- target: canvas -->
Select the shapes to tidy. With nothing selected, Tidy lines works on the whole design.

## Open Tidy lines
<!-- target: ribbon:Home/Tidy lines -->
<!-- wait: window:Tidy lines -->
Click **Tidy lines**.

## Set the distance
<!-- target: text:Join ends closer than -->
Ends closer than this (mm) are joined. Keep it small: it's for gaps too small to see.

## Apply
<!-- target: button:OK -->
<!-- wait: closed:Tidy lines -->
Click **OK**. The status bar says how many lines were joined, closed and removed.

## Find what's still open
<!-- target: ribbon:Home/Select by… -->
**Select by…** → **Open lines (not closed)** selects any lines that are still open.
