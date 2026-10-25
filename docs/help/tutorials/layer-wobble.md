# Wobble
<!-- category: Layers and settings -->
<!-- summary: Make the beam trace small circles along a line, for wider, smoother lines and a wider kerf. -->
<!-- page: layer-settings.md#wobble -->
<!-- order: 140 -->
With wobble on, the beam traces small overlapping circles as it moves along the path. Lines come
out wider and smoother, and cuts get a wider kerf that lets thick material clear its smoke and
melt. It works with either laser, on Cut and Score layers.

## A Cut or Score layer
<!-- target: panel:Layers -->
Click the layer (a Cut or Score layer).

## Turn it on
<!-- target: button:Wobble -->
Under **More settings**, tick **Wobble**. Two settings appear under it.

## Size
<!-- target: field:Wobble size (mm) -->
**Wobble size (mm)**: the diameter of the circles, about how much wider the line or kerf gets.

## Spacing
<!-- target: field:Wobble spacing -->
**Wobble spacing**: how far the circles move forward per loop. Smaller is denser and slower; the
job's time goes up a lot, so check it on the job bar. On long cuts with many passes a fine spacing
makes more points than one job can hold (10 million).

## Too big for one job
<!-- target: panel:Job -->
When that happens on a design of several pieces, the job bar splits it into parts of whole pieces
and shows **Part 1 of N ▾** with the pieces it holds. Ship It sends that part; when it finishes,
the next part is ready. Leave the material where it is between parts. Click the part button to
pick another one, for example to redo a part. One piece alone that's too big can't be split: the
job bar then names the spacing that fits.
