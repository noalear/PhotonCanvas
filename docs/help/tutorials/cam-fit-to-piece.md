# Fit a design to a piece
<!-- category: Camera -->
<!-- summary: Snap a design onto the real piece the camera sees: moved, turned and resized to match. -->
<!-- page: camera.md#fit-to-piece -->
<!-- order: 50 -->
When a design must match a piece already on the bed (a pre-cut blank, a coaster), Fit to piece
moves and turns it so its outline sits on the piece's edges. No measuring by hand.

## Connect and calibrate
<!-- target: panel:Machine -->
This needs the machine connected and the camera calibrated near the piece's height (the
[Calibrate the camera](tutorial:cam-calibrate) tutorial).

## Place the design roughly
<!-- target: canvas -->
<!-- wait: selection -->
Turn on **Show** in the machine panel at the right end of the ribbon, then select the design (it should include the piece's outline)
and drag it roughly over the piece.

## Open Fit to piece
<!-- target: ribbon:Camera/Fit to piece -->
<!-- wait: window:Fit to piece in camera -->
Click **Fit to piece** on the **Camera** tab.

## Set the height
<!-- target: field:Distance (mm) -->
Type the distance to the top of the piece, or click **Auto-focus now**. The line underneath must
show a green ✓.

## Choose how far it may change
<!-- target: field:Turn -->
**Turn**: a little (±30°) or any angle. Tick **Allow resizing (±15 %)** if the piece may differ in
size. **Search (mm)** is how far it may move.

## Fit
<!-- target: button:Take photo and fit -->
Click **Take photo and fit**. Green shows where the design would go; the text says how good the
match is.

## Apply
<!-- target: button:Apply -->
<!-- wait: closed:Fit to piece in camera -->
Click **Apply**. `Ctrl+Z` undoes it.
