# Focus offset
<!-- category: Layers and settings -->
<!-- summary: Move the focus into or above the material for one layer. -->
<!-- page: layer-settings.md#focus-offset -->
<!-- order: 220 -->
The laser is focused on the surface of the material. Moving the focus a little into it can help
a deep cut; moving it above gives a softer, wider beam for gentle marks. **Focus offset (mm)** does
it for one layer, and the head goes back afterwards. Not yet burned on the machine.

## Pick the layer
<!-- target: panel:Layers -->
Click the layer to change. Each layer has its own offset.

## Set the offset
<!-- target: field:Focus offset (mm) -->
Under **More settings**, type the offset: + is deeper into the material (the head goes lower), − is
above it. 0 = at the surface.

## What happens on the machine
The head moves when a layer with a different offset starts, and goes back to where it started at
the end of the job.
