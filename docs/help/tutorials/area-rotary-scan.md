# Scan a part in the rotary
<!-- category: Work area -->
<!-- summary: Turn the part in the rotary a step at a time and measure its top, for a 3D model of the stock. -->
<!-- page: work-area.md#scan-the-part -->
<!-- order: 15 -->
Before carving or marking a part in the rotary (a glued stack, a blank that isn't quite round), scan it
to see its shape and how it sits on the axis. The laser isn't fired, but the rotary turns and the
head moves. Experimental: not yet tried on the machine.

## Choose the rotary
<!-- target: ribbon:Machine/Work mode -->
Scan the part shows in the rotary work mode. Click **Work mode**, choose **Rotary**, check the
**Laser line (mm)** sits over the top of the part, and click **OK**.

## Open Scan the part
<!-- target: ribbon:Machine/Scan the part -->
<!-- wait: window:Scan the part -->
Click **Scan the part** on the **Machine** tab.

## Choose how it measures
<!-- target: field:Measure with -->
**Pointer and close-up camera** reads several places along the laser line (focus on the top of the
part first). **The machine's auto-focus** reads one place, in the middle of the field.

## Calibrate the pointer once
<!-- target: button:Calibrate along the laser line… -->
The pointer needs calibrating once, along the strip a scan reads. Take the part out, lay a flat matt
board on the bed where it was, auto-focus on the board, and click **Calibrate along the laser line…**.
It takes about five minutes. Then put the part back.

## Say where to scan
<!-- target: button:Use the selection -->
Select the shapes that cover the area to scan and click **Use the selection**, or type **From X (mm)**
and **To X (mm)**. The pointer only reads from X 80 to 185 mm (where it was calibrated): a warning
says when places are outside that. Leave **From (°)** 0 and **To (°)** 360 for a whole turn.

## Scan it
<!-- target: button:Scan -->
Click **Scan** and confirm. Keep hands clear of the chuck: **Stop** stops at once, and **Return to
start** then turns it back.

## Standard stock?
<!-- target: field:Stock -->
If the part is a standard stock, say so: for a 2" × 2" square bar choose **Square bar**, type 2 under
**Size** and choose inches. The scan then only finds how the bar sits in the chuck (and the axis
height), and the model is the exact square.

## Set the axis height
<!-- target: field:Axis height (mm) -->
The 3D model needs the height of the rotary's axis. If you don't know it, scan a round rod first, type
its diameter under **Rod Ø (mm)** and click **It was a round rod**.

## Look at the stock
<!-- target: window:Scan the part -->
Turn the 3D view by dragging it, and pick a place along the part with **Place along it** to see it end-on.
**Save as STL…** saves the stock model as a 3D file.
