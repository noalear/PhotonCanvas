# Measure the etch rate
<!-- category: Materials and tests -->
<!-- summary: Find how deep one pass goes, from circles burned 1, 2, 3 … passes until one goes through. -->
<!-- page: materials-and-tests.md#etch-rate -->
<!-- order: 32 -->
Inlays, exact depths and shaped stacked slices need to know how deep one pass goes. This burns the
same setting into small circles, each with more passes; the first one that goes right through gives
the answer. You need a board of known thickness on scrap, and the machine connected.

## Open the etch rate test
<!-- target: ribbon:Laser/Etch rate -->
<!-- wait: window:Etch rate test -->
Put the board on scrap (circles that go through keep burning into what's below), then click **Etch
rate**.

## Set what to measure
<!-- target: field:Power % -->
Power, speed and lines per cm: what you'll use for real.

## Turn the lines
<!-- target: field:Turn per pass ° -->
Turning the fill lines each pass keeps a deep floor even.

## Set the ladder
<!-- target: field:Circles -->
How many circles, and the passes on the first and each next one. The line below gives the passes and
the time.

## Add it
<!-- target: button:Add the test to the design -->
<!-- wait: shapes:+1 -->
It goes in the middle of the bed, and the window closes.

## Burn it round by round
<!-- target: panel:Job -->
<!-- wait: window:Etch rate test -->
Set the job bar's **Passes** to **Round by round**, then **Ship It**. Stay with it. When it's done,
the window opens again.

## Measure the thickness
<!-- target: field:Thickness (mm) -->
Pick the material, and measure its thickness with calipers.

## Click the first through
<!-- target: text:Click the first circle -->
Hold the board up to the light and click the first circle that went right through. Type the depths
of shallower ones if you can measure them.

## Save it
<!-- target: button:Save to the library -->
The depth a pass is kept with the material.
