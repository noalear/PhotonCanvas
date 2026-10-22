# Add, name and order layers
<!-- category: Layers and settings -->
<!-- summary: Add a layer, name and colour it, put it in the right place in the run order, and delete it. -->
<!-- page: layers.md -->
<!-- order: 10 -->
Each layer holds shapes that get the same laser settings, and the list's order is the order they
run in: the top layer first. A layer for each job step (score, engrave, cut) keeps a design easy to
set up and to change.

## The Layers panel
<!-- target: panel:Layers -->
This is the list of layers. Each card says what the layer does, how hard, how fast and how long it
takes. The top one runs first.

## Add a layer
<!-- target: panel:Layers -->
<!-- wait: layers:+1 -->
Click the **+** at the top of the panel (**Add layer**), or press `Ctrl+L`. The new layer goes
just below the active one and becomes active.

## Name it
<!-- target: panel:Layers -->
Double-click the new card, type a name such as "Cut 3 mm", and press `Enter`.

## Give it a colour
<!-- target: panel:Layers -->
Click the coloured bar at the card's left edge and pick a colour. It only changes how the shapes are
drawn on the work area.

## Put it in its place
<!-- target: panel:Layers -->
Drag the card up or down, or use the arrows at the top (**Move up (runs earlier)**, **Move down
(runs later)**). Keep cut layers at the bottom so parts don't move before they're engraved.

## Delete it
<!-- target: panel:Layers -->
With the layer selected, click the bin (**Delete selected layers (shapes move to the layer
above)**). Its shapes move to the layer above. `Ctrl+Z` brings it back.
