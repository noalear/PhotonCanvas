# Lead-in, lead-out and start points
<!-- category: Layers and settings -->
<!-- summary: Keep the start and end marks of each cut off the part, close outlines cleanly, and cut shared edges once. -->
<!-- page: layer-settings.md#overcut-lead-in-and-lead-out -->
<!-- order: 120 -->
Where a cut starts and ends, the beam often leaves a small mark or notch. A lead-in and lead-out
move those into the waste, overcut makes sure the outline closes, and start points put the start
where it does no harm. These work on closed outlines on Cut layers.

## A Cut layer
<!-- target: panel:Layer settings -->
<!-- wait: layer-mode:Cut -->
Select the cut layer and make it a **Cut** layer if it isn't.

## Lead-in
<!-- target: field:Lead-in (mm) -->
Under **More settings**, **Lead-in (mm)**: each outline starts with a short line in the waste
(outside a part, inside a hole), so the start mark isn't on the part.

## Lead-out
<!-- target: field:Lead-out (mm) -->
**Lead-out (mm)**: each outline ends with a short line into the waste, so the end mark isn't on the
part either.

## Overcut
<!-- target: field:Overcut (mm) -->
**Overcut (mm)**: keep cutting this far past the start so the outline closes cleanly. Outlines with
tabs get no lead-in, lead-out or overcut.

## Start near the laser
<!-- target: button:Start outlines nearest the laser -->
Tick **Start outlines nearest the laser** to start each outline where the laser already is: less
travel.

## Choose a start point
<!-- target: ribbon:Home/Start point -->
<!-- wait: tool:Start -->
Or choose **Start point** and click an outline where its cut should start (`Shift`+click clears
it).

## Shared edges
<!-- target: button:Cut shared edges once -->
Parts drawn side by side share an edge. Tick **Cut shared edges once** so it's cut once, not twice.
