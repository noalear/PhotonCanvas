# Outlines over a relief
<!-- category: Pictures -->
<!-- summary: Find the edges in a depthmap and mark them as lines over the relief. -->
<!-- page: pictures.md#depth-outlines -->
<!-- order: 85 -->
A relief from a soft depthmap can look washed out. Depth outlines finds where the surface steps
and marks those edges as lines over the engraving, so the shapes read clearly again.

## Select the depthmap
<!-- target: canvas -->
<!-- wait: picture-selected -->
Click the depthmap picture on the work area. No depthmap yet? **Picture → Depthmap** makes one
from a photo.

## Open Depth outlines
<!-- target: ribbon:Picture/Depth outlines -->
<!-- wait: window:Depth outlines -->
Click **Depth outlines** in the **Relief** group. The preview shows the lines it found over the
depthmap.

## Choose how much to outline
<!-- target: field:How many edges -->
Drag **How many edges** up for more lines, down for only the strongest steps. **Ignore detail
under** decides how small a feature can be and still get a line.

## Choose the line width
<!-- target: button:Swelling and thinning -->
**One laser line** scores a single pass. **All the same width** fills lines of one width.
**Swelling and thinning** makes bold lines on the tall edges and fine ones on the gentle ones.

## Add them
<!-- target: button:Add to design -->
<!-- wait: closed:Depth outlines -->
Click **Add to design**. The lines go on a new **Outlines** layer after the others, so they're
marked over the finished relief.
