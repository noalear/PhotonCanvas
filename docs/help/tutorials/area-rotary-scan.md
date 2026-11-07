# Scan a part in the rotary
<!-- category: Work area -->
<!-- summary: Turn the part in the rotary a step at a time and measure its top, for a 3D model of the stock. -->
<!-- page: work-area.md#scan-the-part -->
<!-- order: 15 -->
Before carving or marking a part in the rotary (a glued stack, a blank that isn't quite round), scan it
to see its shape and how it sits on the axis. The laser isn't fired, but the rotary turns and the
head moves. Experimental: not yet tried on the machine.

## Choose the rotary
<!-- target: ribbon:Laser/Work mode -->
Scan the part shows in the rotary work mode. Click **Work mode**, choose **Rotary**, check the
**Laser line (mm)** sits over the top of the part, and click **OK**.

## Open Scan the part
<!-- target: ribbon:Laser/Scan the part -->
<!-- wait: window:Scan the part -->
Click **Scan the part** on the **Laser** tab.

## Choose how it measures
<!-- target: field:Measure with -->
**Red pointer and close-up camera** reads several places along the laser line (calibrate the pointer
once in **Curved surface**, and focus on the top of the part first). **The machine's auto-focus**
reads one place, in the middle of the field.

## Say where to scan
<!-- target: button:Use the selection -->
Select the shapes that cover the area to scan and click **Use the selection**, or type **From X (mm)**
and **To X (mm)**. Leave **From (°)** 0 and **To (°)** 360 for a whole turn.

## Scan it
<!-- target: button:Scan -->
Click **Scan** and confirm. Keep hands clear of the chuck: **Stop** stops at once, and **Return to
start** then turns it back.

## Set the axis height
<!-- target: field:Axis height (mm) -->
The 3D model needs the height of the rotary's axis. If you don't know it, scan a round rod first, type
its diameter under **Rod Ø (mm)** and click **It was a round rod**.

## Look at the stock
<!-- target: window:Scan the part -->
Turn the 3D view by dragging it, and pick a place along the part with **Place along it** to see it end-on.
**Save as STL…** saves the stock model as a 3D file.
