# Engrave to an exact depth
<!-- category: Materials and tests -->
<!-- summary: Burn in rounds, measuring the depth with the red pointer between them, until it's right. -->
<!-- page: material-tests.md -->
<!-- order: 34 -->
Guessing the passes for a precise depth rarely works: each pass takes off less as the hole deepens.
Exact depth measures the depth with the red pointer after each round and works out the next. You need
the machine connected, the focus set on the material, and the pointer calibrated once (**Machine → Curved surface → Calibrate the pointer…**).

## Select the shapes
<!-- target: canvas -->
<!-- wait: selection -->
Select the closed shapes to engrave deep. Holes narrower than about 5 mm can't be measured.

## Open Exact depth
<!-- target: ribbon:Tests/Exact depth -->
<!-- wait: window:Exact depth -->
Click **Tests** on the **Materials & Tests** tab, pick **Exact depth** in the list and click **Open**.

## Set the depth
<!-- target: field:Depth (mm) -->
The depth to reach, and under **Within (mm)** how close is close enough.

## Check the first guess
<!-- target: field:About this deep a pass (mm) -->
It comes from your etch-rate test if you have one. Only the first round uses it.

## Start
<!-- target: button:Start: measure points chosen, a layer of its own -->
The shapes move to a layer of their own, and the steps appear.

## Measure the surface
<!-- target: button:Measure the surface -->
The pointer reads the height at every dot before anything is burned.

## Burn a round
Click **Set up … passes and close**, then frame it and **Ship It**. The window opens again when the
job is done.

## Measure the depth
<!-- target: button:Measure the depth -->
Brush the dust and char out of the hole first. The next round is worked out from what was taken off.

## Repeat, then finish
<!-- target: button:Finish (keep the shapes on their layer) -->
Repeat rounds until it says **Done**, then click **Finish**.
