# Teach PhotonCanvas where the close-up camera looks
<!-- category: Camera -->
<!-- summary: Burn ten dots, find them in a close-up photo, and save where the close-up camera looks. -->
<!-- page: camera.md#close-up-position -->
<!-- order: 40 -->
Once PhotonCanvas knows exactly where the close-up camera looks, tone and line tests are read from
sharp close-up photos, and close-up photos land on the work area by themselves. Do it once for each
focus height you use.

## Connect and focus
<!-- target: panel:Machine -->
This needs the machine connected (see [The machine and settings](machine.md)). Put flat scrap under
the head and focus on it at the height you'll use.

## Open Close-up position
<!-- target: ribbon:Camera/Close-up position -->
<!-- wait: window:Close-up camera position -->
Click **Close-up position** on the **Camera** tab.

## See what the camera sees
<!-- target: button:Take a look -->
Click **Take a look**, and move the scrap so it fills the picture.

## Size the dots
<!-- target: field:Apart (mm) -->
The defaults are 8 mm apart and 2 mm across. All ten dots must fit in the picture with room to
spare.

## Add the dots
<!-- target: button:Add the dots to the design -->
<!-- wait: closed:Close-up camera position -->
Click **Add the dots to the design**. They go on a "Close-up marks" layer, and the window closes.

## Check where they land
<!-- target: ribbon:Laser/Frame -->
Click **Frame** and watch the close-up camera live (**Close-up** in the machine panel, then **Live**): the red light
should trace the dots well inside the picture. Move them if not.

## Burn them
<!-- target: ribbon:Laser/Ship It -->
Set the "Close-up marks" layer's power so the dots come out dark, click **Ship It** and press the
machine's start button.

## Find them
<!-- target: button:Take a close-up photo -->
<!-- wait: window:Close-up camera position -->
When the job finishes the window opens again on **Find the dots**. Don't move the scrap. Click
**Take a close-up photo**: found dots are ringed in green and numbered.

## Save
<!-- target: button:Save where the close-up camera looks -->
Click **Save where the close-up camera looks**, then **Remove the dots from the design**.
