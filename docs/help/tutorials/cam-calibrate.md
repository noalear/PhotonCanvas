# Calibrate the camera
<!-- category: Camera -->
<!-- summary: Line the overview camera's picture up with where the laser marks, from 25 burned marks. -->
<!-- page: camera.md#calibrate-the-camera -->
<!-- order: 10 -->
Calibrating lets the camera picture lie under your design at true size, so you see exactly where a
job will land. It takes about 5 minutes and a scrap sheet, once for each material height you use.

## Connect and lay out scrap
<!-- target: panel:Machine -->
This needs the machine connected (see [The machine and settings](machine.md)). Lay a flat scrap
sheet of wood or card over the whole 220 × 220 mm bed.

## Open the calibration
<!-- target: ribbon:Machine/Calibrate camera -->
<!-- wait: window:Camera calibration -->
Click **Calibrate camera** on the **Machine** tab.

## Focus on the scrap
<!-- target: button:Auto-focus on the material -->
Click **Auto-focus on the material**. The head moves; the line underneath says where it focused.

## Choose the marks
<!-- target: field:Mark size -->
The defaults are dots, 2 mm across, 30 % power at 150 mm/s. Slower makes darker marks.

## Burn them
<!-- target: button:Send marks to machine -->
Click **Send marks to machine**, then **Upload**. Press the machine's start button and wait for the
burn to finish.

## Photograph them
<!-- target: button:Take photo -->
Click **Take photo**. The photo appears on the left, zoomed in where the first corner is expected.

## Click the four corners
<!-- target: text:click the centre of the -->
Click the top-left, top-right, bottom-right and bottom-left marks in turn. Near enough is fine: the
clicks snap to the marks, and the other 21 are found by themselves.

## Check and save
<!-- target: button:Save calibration -->
Check every marker is green and the result says Excellent or Good, then click **Save calibration**.
It's kept for the focus distance shown in **Distance**.
