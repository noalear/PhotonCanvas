# Measure the material's thickness
<!-- category: Camera -->
<!-- summary: Auto-focus on the material and compare with the empty bed to get its thickness. -->
<!-- page: camera.md#thickness -->
<!-- order: 90 -->
Thickness tells you how thick a board or sheet is without calipers: the machine focuses on it and
PhotonCanvas compares that with the empty bed, measured once.

## Connect
<!-- target: panel:Machine -->
This needs the machine connected (see [The machine and settings](machine.md)).

## Measure the empty bed (first time only)
<!-- target: ribbon:Camera/Thickness -->
Clear the bed under the head, click **Thickness** on the **Camera** tab, and choose
**Measure the empty bed**. The head moves to focus on the bed.

## Measure the material
<!-- target: ribbon:Camera/Thickness -->
Put the material under the head and click **Thickness** again. The machine focuses on it.

## Read it
The thickness shows in a message. If it's far off (the bed was moved or changed), choose
**Measure the empty bed again**.
