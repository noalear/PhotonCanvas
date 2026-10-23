# Two operations on the same shapes
<!-- category: Layers and settings -->
<!-- summary: Add a sub-layer to engrave and outline, or cut and fill, the same shapes with different settings. -->
<!-- page: layers.md#sub-layers -->
<!-- order: 50 -->
A sub-layer does a second thing to the shapes of the layer above it, with its own settings: an
outline traced round an engraving, a fill inside a cut. It has no shapes of its own, so the shapes
are never doubled up. You need a layer with shapes on it.

## Add the sub-layer
<!-- target: panel:Layers -->
<!-- wait: layers:+1 -->
Right-click a layer that has shapes and choose **Add sub-layer (another operation on these
shapes)**. Under an Engrave layer it's a Score layer; under a Cut or Score layer, an Engrave layer.

## Its card
<!-- target: panel:Layers -->
The new card is indented under its parent and reads "↳ sub-layer" where other cards count their
shapes. Point at that to see the parent's name. Whatever is on the parent layer, it does too.

## Set it up
<!-- target: panel:Layer settings -->
Choose its mode, power and speed like any layer. It started with the parent's laser and material.

## Choose when it runs
<!-- target: panel:Layers -->
Like any layer, it runs where it is in the list. Drag it above its parent to run it first.

## Undo it
<!-- target: panel:Layers -->
Right-click it and choose **Make it a normal layer (with no shapes)**, or delete it with the bin.
Deleting the parent deletes its sub-layers too.
