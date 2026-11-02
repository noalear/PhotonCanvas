# Make a finger-jointed box
<!-- category: Drawing -->
<!-- summary: Draw the panels of a box whose finger joints slot together, from its inside size. -->
<!-- page: generators.md#box -->
<!-- order: 60 -->
The box generator draws every panel of a box with finger joints that slot together, sized from the
inside measurements and your material's thickness. Cut the panels, push them together and glue.

## Open the generators
<!-- target: ribbon:Home/Generators -->
<!-- wait: window:Generators -->
Click **Generators** on the **Home** tab.

## Choose Box
<!-- target: tabitem:Box -->
Click the **Box** tab.

## Pick the cut settings
<!-- target: field:Cut with -->
Under **Cut with**, pick your material's cut recipe from the library. One with a kerf measured puts the panels on a layer that allows for it, so the fingers press together snugly, and sets the thickness.

## Type the inside size
<!-- target: field:Inside width -->
Type the inside measurements in mm: **Inside width**, **depth** and **Inside height**.

## Set the thickness
<!-- target: field:Material -->
Check **Material** is your sheet's thickness. Measure it: the joints depend on it.

## Set the fingers
<!-- target: field:Finger width -->
About three times the thickness is a good **Finger width**. **With a lid** adds a sixth panel.

## Make it
<!-- target: button:Make the box -->
<!-- wait: closed:Generators -->
Click **Make the box**. The panels appear in a row, on a Cut layer.

## Get it ready to cut
<!-- target: panel:Layer settings -->
A box is usually wider than the work area: ungroup and arrange the panels. Without a recipe, set the Cut layer's kerf (in **More settings**) so the fingers fit snugly.
