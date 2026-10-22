# Focus on one spot with the pointer
<!-- category: Camera -->
<!-- summary: Aimed focus: the red pointer and the close-up camera read the height at the selection, and the head focuses there. -->
<!-- page: camera.md#focus-on-selection -->
<!-- order: 80 -->
On an uneven piece, auto-focus measures only under the head. Focus on selection reads the height
right where your design is, with the red pointer; nothing fires and nothing touches the material.
Not yet tried on the machine.

## Connect and calibrate the pointer
<!-- target: panel:Machine -->
This needs the machine connected (see [The machine and settings](machine.md)) and the red pointer
calibrated once, in **Laser → Curved surface** (the
[Calibrate the red pointer](tutorial:area-calibrate-pointer) tutorial).

## Focus near the spot
<!-- target: panel:Machine -->
Click **Auto-focus** at the right end of the ribbon (or type a height). The pointer reads heights
within 8 mm of the current focus.

## Select the design
<!-- target: canvas -->
<!-- wait: selection -->
Select the part of the design to focus on. With nothing selected, it focuses on the middle of the
bed.

## Focus on it
<!-- target: ribbon:Camera/Focus on selection -->
Click **Focus on selection** on the **Camera** tab. The pointer is aimed at the middle of the
selection and the head moves to the height it reads.

## Read the result
The status bar says the new focus and how far it moved. If the dot couldn't be read, auto-focus
nearer the spot first, or pick a spot the close-up camera can see.
