# Spread the heat across a colour test
<!-- category: Running jobs -->
<!-- summary: Run many small squares where the plate is coolest, so thin metal stays flat and in focus. -->
<!-- page: heat-order.md#across-the-whole-job -->
<!-- order: 35 -->
Thin metal bows and goes out of focus when square after square heats the same corner. **Heat** in
the job bar runs each layer (and each separate shape on an engrave layer) where the plate is
coolest, and waits only where it must. You need a design with several engrave layers or squares,
such as a colour test. Not yet tried on the machine: tune it by results.

## Open the heat settings
<!-- target: button:Heat -->
Click **Heat** in the job bar, beside **Order**. Its settings open above it; the next steps are all
in there.

## Turn it on
<!-- target: button:Heat -->
Tick **Spread the heat across layers and objects**. The chip now reads **Heat: spread**, and the
design keeps the setting.

## The plate
<!-- target: button:Heat -->
Choose the **Sheet metal** and its **Thickness (mm)**. Stainless and titanium hold heat where it
went in much longer than aluminium.

## How it's held
<!-- target: button:Heat -->
**How it's held**: taped or clamped to a metal block, the plate cools many times faster than on the honeycomb, so the
job needs far fewer waits.

## How warm it may get
<!-- target: button:Heat -->
**Keep warming under (°C)**: no part starts on plate warmer than this over where it began. 25 is a start; lower waits more.

## Read the result
<!-- target: panel:Job -->
The line under the settings, and **Checks**, say how many parts were reordered, how warm the plate
gets and how much waiting was added. If the waits are long, add a heat sink.

## See the order
<!-- target: button:Order -->
Click **Order**: the squares are coloured from first (blue) to last (red), spread across the plate.
