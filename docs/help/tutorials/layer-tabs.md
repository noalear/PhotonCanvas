# Tabs: keep parts in the sheet
<!-- category: Layers and settings -->
<!-- summary: Leave small bridges in each cut so parts stay put until you snap them out. -->
<!-- page: layer-settings.md#tabs -->
<!-- order: 110 -->
Small parts that drop through the bed, or shift once they're loose, spoil the rest of the job.
Tabs leave a few short uncut bridges in each outline so the parts stay in the sheet; you snap them
out afterwards. They work on Cut layers.

## A Cut layer
<!-- target: panel:Layer settings -->
<!-- wait: layer-mode:Cut -->
Select the layer with the parts and make it a **Cut** layer if it isn't.

## How many tabs
<!-- target: field:Tabs -->
Under **More settings**, set **Tabs**: how many bridges in each closed outline (0 = none). They're
spaced evenly and kept off sharp corners.

## How wide
<!-- target: field:Tab width (mm) -->
**Tab width (mm)**: the width of each bridge. Thin material needs less; 0.5 mm is a start.

## Bridges that snap
<!-- target: field:Tab power % -->
**Tab power %** above 0 cuts the tabs too, at this low power: thin bridges that snap out cleanly.
0 leaves them uncut.

## Tabs exactly where you want them
<!-- target: ribbon:Home/Place tabs -->
<!-- wait: tool:Tabs -->
Choose **Place tabs** and click an outline to put a tab there (`Shift`+click removes one). Those
outlines get exactly those tabs.
