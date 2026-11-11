# Mark a tray of parts
<!-- category: Arranging -->
<!-- summary: Mark the same design, each with its own serial number, on a tray of parts at a set pitch, and skip empty pockets. -->
<!-- page: devicearray.md -->
<!-- order: 65 -->
A device array marks one design on every part in a tray or jig, at the tray's pitch, and numbers
them one after another. It hasn't been tried on the machine yet: mark a tray of scrap parts first.

## Select the design for one part
<!-- target: canvas -->
<!-- wait: selection -->
Draw the design on the first part (top left of the tray), with `{serial}` in its text if each part
gets its own number, and select it.

## Open Device array
<!-- target: ribbon:Arrange/Device array -->
<!-- wait: window:Device array -->
Click **Device array** on the **Arrange** tab (in the **Repeat** group).

## How many parts
<!-- target: field:Columns -->
Set **Columns** and **Rows** to the tray's pockets. Or set **Size by** to **An area** and give the
area's size: as many as fit are made.

## The pitch
<!-- target: field:Pitch X (mm) -->
Type the tray's spacing, centre to centre, in **Pitch X (mm)** and **Pitch Y (mm)**. The copies
move on the work area as you type.

## Skip empty pockets
<!-- target: field:Count -->
Click a square under **Devices** to turn that part off; drag across several. **Count** sets the order
the serial numbers go in.

## Make it
<!-- target: button:Make the array -->
<!-- wait: closed:Device array -->
Click **Make the array**. Edit any device on the canvas and they all follow; right-click one and
choose **Turn device off** to skip it. Frame the job before marking.
