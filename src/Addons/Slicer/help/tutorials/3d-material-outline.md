# Slice onto the board in the machine
<!-- category: 3D and slices -->
<!-- summary: Outline the board or offcut you have, and lay the slices out on it instead of the whole bed. -->
<!-- page: 3d-slices.md#where-the-parts-go -->
<!-- order: 70 -->
Slices can fill the actual board in the machine, an offcut or a scrap with a knot, instead of the
whole bed. Outline it, mark it as the material, and the slicer packs the parts inside it. Not yet
tried on the machine.

## Photograph the board
<!-- target: panel:Machine -->
Put the board in the machine and click **Photo** in the machine panel at the right end of the ribbon (the machine must be connected and the
camera calibrated). The photo lies under your design.

## Outline the board
<!-- target: ribbon:Home/Pick piece -->
<!-- wait: shapes:+1 -->
Choose **Pick piece** and click inside the board on the photo. Without a camera, draw the board with
**Rectangle** at its real size and place instead.

## Make it the material outline
<!-- target: canvas -->
Right-click the outline and choose **Use as material outline**. It moves to the **Material**
layer, which is never sent.

## Open 3D to slices
<!-- target: ribbon:Home/3D to slices -->
<!-- wait: window:Stacked slices -->
Click **3D to slices** on the **Home** tab.

## Open a model
<!-- target: button:Open a 3D model… -->
Click **Open a 3D model…** and double-click a model. Press **Next** when it shows in the preview.

## Choose the board
<!-- target: field:Cut from -->
Your outline is chosen already. The summary warns if the biggest part won't fit on it.

## Make the slices
<!-- target: button:Make the slices -->
Click **Make the slices**. The parts fill the board around anything already there; more boards
go on new pages with the same outline.
