# Make an inlay
<!-- category: Materials and tests -->
<!-- summary: Turn shapes into a pocket in the board and a piece of veneer that sits in it flush. -->
<!-- page: material-tests.md -->
<!-- order: 50 -->
An inlay needs a pocket exactly as deep as the veneer and a piece exactly the pocket's size. The inlay
maker makes both from one design, using your etch-rate result for the depth and your cut test's kerf
for the size. Run those two tests on your board and veneer first.

## Select the shapes
<!-- target: canvas -->
<!-- wait: selection -->
Select the closed shapes to inlay.

## Open Inlay
<!-- target: ribbon:Tests/Inlay -->
<!-- wait: window:Inlay -->
Click **Tests** on the **Materials & Tests** tab, pick **Inlay** in the list and click **Open**.

## Veneer thickness
<!-- target: field:Veneer (mm) -->
Measure the veneer with calipers: the pocket goes this deep. **Gap each side (mm)** leaves room for
glue.

## Pocket settings
<!-- target: field:Engrave with -->
Pick your etch-rate recipe for the board: its depth a pass sets how many passes.

## Piece settings
<!-- target: field:Cut with -->
Pick your cut recipe for the veneer: its kerf makes the piece the drawn size.

## Make them
<!-- target: button:Make the pocket and the piece -->
The pocket goes on this page, the piece on a new page, **Inlay piece**, in the same place.

## Burn, then cut
<!-- target: text:Inlay piece -->
Burn the pocket into the board. Then put the veneer in, switch to the **Inlay piece** page and cut.
