# Picture layer settings
<!-- category: Layers and settings -->
<!-- summary: Set how a picture is burned: resolution, picture mode, dithering, screens and dot time. -->
<!-- page: layer-settings.md#picture-settings -->
<!-- order: 190 -->
A Picture layer turns a picture into dots, one laser pulse each. The resolution, the way grey
tones become dots and how long each dot fires decide how it comes out. You need a picture: Home →
**Picture** (`Ctrl+J`) imports one.

## Select the picture
<!-- target: canvas -->
<!-- wait: picture-selected -->
Click the picture on the work area. Its layer's settings show in **Layer settings**.

## A Picture layer
<!-- target: panel:Layer settings -->
<!-- wait: layer-mode:Image -->
Its layer must be a Picture layer: the **Picture** button at the top is lit. If not, click it.

## Resolution
<!-- target: field:Resolution -->
**Resolution**: dots per inch. 150–300 suits most wood; the line under it gives the dot spacing.

## Picture mode
<!-- target: field:Picture mode -->
**Picture mode**: **Dots (dithered)** (every dot at the same power), **Greyscale (experimental)**
(each dot's power follows the picture) or **Depth slices (relief)**.

## Dithering
<!-- target: field:Dithering -->
**Dithering**: how greys become dots. **Jarvis (smooth)** for photos, **Threshold (line art)** for
logos, **Halftone (dots)** or **Line screen (engraved lines)** for a printed or engraved look.

## Screen size
<!-- target: field:Cells / inch -->
With Halftone or Line screen, **Cells / inch** sets how big the dots or lines are and **Screen angle
°** their direction.

## Dot time
<!-- target: field:Dot time µs -->
Under **More settings**, **Dot time µs**: how long each dot fires. Longer is darker.

## Test it
<!-- target: ribbon:Laser/Tone test -->
Laser → **Tone test** burns grey patches with this layer's settings and corrects your pictures for
the material.
