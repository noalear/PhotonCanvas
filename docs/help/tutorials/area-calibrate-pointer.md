# Calibrate the pointer
<!-- category: Work area -->
<!-- summary: Teach PhotonCanvas to read heights from where the pointer's spot lands (once, on a flat board). -->
<!-- page: work-area.md#calibrate-the-pointer -->
<!-- order: 20 -->
Measuring a curved piece, Scan the part and Focus on selection all read heights from where the blue
pointer's spot shows in the close-up camera. This calibration, done once on a flat board, is what makes that work. Not yet tried
on the machine.

## Connect and lay a board
<!-- target: panel:Machine -->
This needs the machine connected (see [The machine and settings](machine.md)). Lay a flat, matt
board (not shiny) across the middle of the bed, about 95 × 30 mm (X 85 to 180, Y 95 to 125). On the
rotary, take the part out and lay the board where it goes.

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
Click **Calibrate…**, then **Start**. The head moves up and down and the pointer stops on
44 spots along the middle of the bed (the green strip on the left) for a photo at each height; the
laser isn't fired. About seven minutes.

## Done
<!-- target: window:Curved surface -->
Step 1 now says **✓ Calibrated** with the board's thickness and the date. It's kept for good. Click **Cancel** if you don't want
to measure a piece now.
