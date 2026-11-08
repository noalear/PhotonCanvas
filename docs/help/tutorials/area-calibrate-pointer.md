# Calibrate the red pointer
<!-- category: Work area -->
<!-- summary: Teach PhotonCanvas to read heights from where the red pointer's dot lands (once, on a flat board). -->
<!-- page: work-area.md#calibrate-the-pointer -->
<!-- order: 20 -->
Measuring a curved piece and Focus on selection both read heights from the red pointer's dot in the
close-up camera. This calibration, done once on a flat board, is what makes that work. Not yet tried
on the machine.

## Connect and lay a board
<!-- target: panel:Machine -->
This needs the machine connected (see [The machine and settings](machine.md)). Lay a flat, matt
board (not shiny) under the head, covering the middle of the bed.

## Open Curved surface
<!-- target: ribbon:Machine/Curved surface -->
<!-- wait: window:Curved surface -->
Click **Curved surface** on the **Machine** tab.

## Set the board's thickness
<!-- target: field:Board thickness -->
Step **1 · Calibrate the pointer (once)** is at the top on the right. Click **Auto-focus on the
board** to have the machine measure it, or measure the board with calipers and type its thickness in
**Board thickness**. Get it right: any error here is added to every height measured later.

## Calibrate
<!-- target: button:Calibrate -->
Click **Calibrate…**, then **Start**. The head moves up and down and the pointer is
aimed at 25 points in the middle of the bed (the green square on the left); the laser isn't fired.
About two minutes.

## Done
<!-- target: window:Curved surface -->
Step 1 now says **✓ Calibrated** with the board's thickness and the date. It's kept for good. Click **Cancel** if you don't want
to measure a piece now.
