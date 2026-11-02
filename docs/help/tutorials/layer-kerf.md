# Kerf: parts at their drawn size
<!-- category: Layers and settings -->
<!-- summary: Make cut parts and holes come out the size you drew them, by allowing for the width of the cut. -->
<!-- page: layer-settings.md#kerf -->
<!-- order: 100 -->
The beam burns away a thin line of material, so a part cut along its outline comes out smaller by
half that width all round, and a hole bigger. Setting the kerf moves the cut outward on parts and
inward on holes, so both come out at the size you drew.

## Measure your kerf
Laser → **Cut test** (from the Material tests add-on) finds the settings that cut your material and measures the kerf, and saves it
with the material. Or cut a square and measure it: the kerf is how much narrower it came out than
drawn.

## A cut layer
<!-- target: panel:Layers -->
Click the cut layer (kerf works on Cut and Score layers).

## Set the kerf
<!-- target: field:Kerf (mm) -->
Type the kerf in mm, for example 0.15. Outlines move out by half of it, holes in by half. 0 turns
it off.

## Read the checks
<!-- target: panel:Job -->
Open paths are cut as drawn, and outlines narrower than the kerf are left out: the job's checks say
how many.
