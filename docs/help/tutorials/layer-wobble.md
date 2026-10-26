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
makes a big job (69 million points, 1.2 GB, for 4 m of cuts × 7 passes at 0.015 mm), slower to
build and upload. Past about 2 GB, the most one job can be, the job bar tells you the spacing that
fits.
