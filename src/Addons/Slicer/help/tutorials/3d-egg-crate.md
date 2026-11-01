# Build a model as an egg crate
<!-- category: 3D and slices -->
<!-- summary: Cut a model as upright sheets that slot together into an open grid of its shape. -->
<!-- page: 3d-slices.md#egg-crate -->
<!-- order: 50 -->
An egg crate builds a model from upright sheets that cross both ways, slotted half-way so they
slide together. It takes few parts and little material, so it suits big models. Not yet tried on
the machine.

## Open 3D to slices
<!-- target: ribbon:Home/3D to slices -->
<!-- wait: window:Stacked slices -->
Click **3D to slices** on the **Home** tab.

## Open a model
<!-- target: button:Open a 3D model… -->
Click **Open a 3D model…** and double-click a model. Press **Next** when it shows in the preview.

## Build it as an egg crate
<!-- target: field:Build as -->
Choose **Egg crate (slotted grid)**. The preview shows the sheets standing in place.

## Sheets across the width
<!-- target: field:Sheets across X -->
Choose how many sheets stand across the model's width. More sheets follow the shape more closely.

## Sheets across the depth
<!-- target: field:Sheets across Y -->
Choose how many stand across its depth.

## Set the slot fit
<!-- target: field:Slot fit (mm) -->
Slots are the material's thickness plus this. Raise it if sheets won't go together, lower it if
they're loose.

## Name the sheets
<!-- target: button:Name each sheet and mark what goes in each slot -->
Leave this ticked. Each sheet gets its name (X1, Y2…), and each slot the name of the sheet that goes
in it.

## Make the slices
<!-- target: button:Make the slices -->
Click **Make the slices**. To build it, stand the X sheets up and lower the Y sheets onto them.
