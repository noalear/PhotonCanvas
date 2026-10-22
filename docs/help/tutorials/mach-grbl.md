# Add a GRBL machine
<!-- category: Machine and settings -->
<!-- summary: Describe a GRBL laser and send jobs to it over USB. -->
<!-- page: machine.md#grbl-machines -->
<!-- order: 60 -->
PhotonCanvas can also send jobs to a laser with a GRBL controller, over USB. Describe it once, then
choose it as the machine in use. Have its controller settings to hand. Not yet tried on a real GRBL
machine.

## Open Settings
<!-- target: header:Settings -->
<!-- wait: window:Settings -->
Click the gear at the top right.

## Open the Machines page
<!-- target: text:Machines -->
Click **Machines** in the list on the left.

## Add one
<!-- target: button:Add a GRBL machine -->
Click **Add a GRBL machine**. Its settings open below the list.

## Describe it
<!-- target: field:Bed width (mm) -->
Set the bed size, the laser, **Fastest (mm/s)**, **Full power is S** and **Where 0, 0 is** from
your controller's settings.

## Pick its port
<!-- target: field:Port -->
Plug the machine in, then pick its port. **Speed (baud)** is usually 115200.

## Use it
<!-- target: text:My laser (GRBL) -->
Click the round button beside it. **Ship It** now opens the GRBL sender: connect, home, frame and
start from there.
