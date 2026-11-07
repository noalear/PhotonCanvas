# Spread the heat on thin metal
<!-- category: Layers and settings -->
<!-- summary: Cut thin sheet metal in an order that keeps it cool and flat, waiting only when needed. -->
<!-- page: heat-order.md#turning-it-on -->
<!-- order: 240 -->
Thin sheet metal warps when cuts pile heat into one area. **Spread the heat** works out how the
sheet warms as the job runs and cuts every pass of every path where it's coolest, waiting only when
it must. Not yet tried on the machine: tune it by how flat your parts come out. You need a Cut or
Score layer with the parts.

## Pick the cut layer
<!-- target: panel:Layers -->
Click the Cut or Score layer with the metal parts.

## Choose Spread the heat
<!-- target: panel:Layer settings -->
In the **Order** list (under power and speed), choose **Spread the heat**. Its settings appear
under it.

## The metal
<!-- target: field:Sheet -->
**Sheet**: your metal. Heat spreads twenty times faster in aluminium than in stainless steel, so
stainless needs more spreading out.

## Its thickness
<!-- target: field:Thickness mm -->
**Thickness mm**: thinner sheet warms more from the same cut.

## How warm it may get
<!-- target: field:Keep under +°C -->
**Keep under +°C**: how much warmer any patch may get. Lower is flatter, with more waiting. 30 is
a start.

## Long paths in pieces
<!-- target: field:Pieces of mm -->
**Pieces of mm**: longer paths are cut in pieces of about this length, at different times. 0 cuts
them whole.

## Read the checks
<!-- target: panel:Job -->
The job's checks say how warm the sheet gets and how much waiting it adds. If one cut alone is over
the limit, lower the power, go faster or allow more warming.

## See the order
<!-- target: ribbon:View/Processing order -->
Turn on **Processing order** (View tab): the pieces are coloured from first (blue) to last (red), from the middle
outwards.
