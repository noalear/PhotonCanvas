# Use your cut test on the slices
<!-- category: 3D and slices -->
<!-- summary: Take the material's thickness, kerf and slot fit from your cut test, so parts and slots come out the right size. -->
<!-- page: 3d-slices.md#using-your-cut-test-thickness-kerf-and-slot-fit -->
<!-- order: 40 -->
The cut test (Laser → Cut test) finds the settings that cut your material and measures the kerf
and the slot fit, saved with the material in the library. Run one on your sheet first, and the
slicer uses it so pins fit and slots slide together (not yet tried on the machine).

## Open 3D to slices
<!-- target: ribbon:Home/3D to slices -->
<!-- wait: window:Stacked slices -->
Click **3D to slices** on the **Home** tab.

## Open a model
<!-- target: button:Open a 3D model… -->
Click **Open a 3D model…** and double-click a model. Press **Next** when it shows in the preview.

## Choose your cut settings
<!-- target: field:Cut with -->
Choose your material's cut settings. The line under it says what came with them: thickness, kerf
and slot fit.

## Check the thickness
<!-- target: field:Material (mm) -->
It now shows the thickness saved with the material. Change it if this sheet measures differently.

## Check the slot fit
<!-- target: field:Slot fit (mm) -->
For egg crate and radial, slots are the material's thickness plus this. It shows your cut test's
fit.

## Make the slices
<!-- target: button:Make the slices -->
Click **Make the slices**. The cut settings, kerf included, go on the **Slices · cut** layer.

## Check the cut layer
<!-- target: panel:Layers -->
Close the window and look at **Slices · cut**: it has the cut settings and kerf you chose, on every
page the parts went on.
