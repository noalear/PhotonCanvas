# Trace an edge with the camera
<!-- category: Drawing -->
<!-- summary: Click along an edge on the camera picture and the line snaps to it. -->
<!-- page: drawing.md#trace-edge -->
<!-- order: 75 -->
**Trace edge** is a magnetic pen: click along an edge on the camera picture and the line follows
it. Use it when **Pick piece** can't tell the piece from what's around it, or for part of an edge.

## Show the camera picture
<!-- target: panel:Machine -->
This needs the machine connected and the camera calibrated. Put the piece on the bed and click **Show** in the machine panel at the right end of the ribbon.

## Choose Trace edge
<!-- target: ribbon:Placement/Trace edge -->
<!-- wait: tool:Magnetic -->
Click the lasso icon, **Trace edge**, on the **Placement** tab (or press `M`).

## Click along the edge
<!-- target: canvas -->
Click on the edge, then every few centimetres along it. The line between clicks follows the edge.

## Set the smoothing
<!-- target: field:Corners -->
Before you finish, set **Smoothness** and **Corners**: bends sharper than **Corners** stay corners, gentler ones become curves.

## Close it
<!-- target: canvas -->
<!-- wait: shapes:+1 -->
Click the first point to close the shape, or press `Enter` for an open line. `Backspace` removes the last point.
