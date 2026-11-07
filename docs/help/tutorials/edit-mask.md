# Mask shapes with a shape
<!-- category: Editing shapes -->
<!-- summary: Keep lines and shapes only inside (or outside) another shape. -->
<!-- page: editing-shapes.md#mask-with-shape -->
<!-- order: 105 -->
Mask with shape cuts away everything past another shape's edge: keep depth outlines, a pattern or
hatching only inside a frame or some lettering.

## Select the shapes and the mask
<!-- target: canvas -->
<!-- wait: selection:2 -->
Select what to cut and the shape to cut it to: drag a box round them, or `Shift`+click each.

## Open Mask with shape
<!-- target: ribbon:Home/Mask with shape -->
<!-- wait: window:Mask with shape -->
Click **Mask with shape** in the **Shape builder** group. The preview shows what's kept in dark
brown and the mask dashed in blue.

## Check the mask
<!-- target: field:Mask -->
Make sure the right shape is the mask. It starts on the simplest one selected; pick another here.

## Inside or outside
<!-- target: button:Keep what's inside the mask -->
Keep what's inside the mask, or what's outside it. **The mask afterwards** says what happens to the
mask: it moves to the mask layer, where it's kept but never burned (the default), stays on its own
layer to be burned too, or is deleted.

## Apply it
<!-- target: button:Mask -->
<!-- wait: closed:Mask with shape -->
Click **Mask**. `Ctrl+Z` undoes it.
