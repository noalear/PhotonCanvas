# Path order
<!-- category: Layers and settings -->
<!-- summary: Choose the order a cut or score layer's paths run in, and see it on the work area. -->
<!-- page: layer-settings.md#order-cut-and-score -->
<!-- order: 90 -->
Inside a cut layer, the order of the paths matters: holes should be cut before the part around
them drops, and smoke should drift over what's finished. **Order** chooses how the paths are
sorted. You need a Cut or Score layer with a few shapes.

## Pick the cut layer
<!-- target: panel:Layers -->
Click a Cut or Score layer. **Order** only shows for those.

## Choose the order
<!-- target: panel:Layer settings -->
Open the **Order** list, under power and speed:
- **Least travel (insides first)**: holes first, then the nearest next. The usual choice.
- **As drawn**: the order you drew them; holes aren't moved first.
- **Back to front (away from smoke)**: insides first, then from the back of the work area forward.
- **Spread the heat**: for thin sheet metal that warps.

## See the order
<!-- target: ribbon:View/Processing order -->
Turn on **Processing order** (View tab): the paths are coloured from first (blue) to last (red), with the travel
moves dashed.

## Start where the laser is
<!-- target: button:Start outlines nearest the laser -->
Under **More settings**, **Start outlines nearest the laser** starts each closed outline at its
point nearest the laser: less travel.
