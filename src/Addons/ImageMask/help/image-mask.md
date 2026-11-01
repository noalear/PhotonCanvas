# Image mask

An add-on (Settings → Add-ons → **Image mask**): on when PhotonCanvas is installed, and can be turned off. Its buttons are **Picture → Shape → Image mask** and **Remove mask**. A picture masked before keeps its mask (it's shown and engraved masked) while the add-on is off.

## Image mask

**Image mask** shows and engraves only the part of a picture inside a closed shape: a photo in a
circle, a heart, a frame. The picture itself isn't changed.

1. Draw or import the closed shape over the picture.
2. Select the picture and the shape together (`Shift`+click).
3. Click **Image mask**.

- The mask moves, resizes and turns with the picture, and survives filters, crop and the eraser.
- Holes in the shape stay holes in the picture. A new mask replaces the old one.
- A shape from another program whose outline comes back to its start point counts as closed, even
  without a closing command in the file. If the shape really is open (its ends don't meet), the
  status bar says so: close it with **Tidy lines** first.
- Only the masked part has to fit the work area: the rest of the picture can hang outside it, even
  when its rows are turned to an angle (**Split onto pieces** relies on this).
- Pictures are always drawn under lines and shapes, so the outline you line a picture up with stays
  in sight.
- The shape itself stays, as a guide. Delete it, or turn its layer's output off, if it shouldn't be
  marked.
- The picture's own engraving (dots or depth slices) and **Engraving** lines follow the mask.
  **Trace**, **Centre-line trace**, **Colour on metal**, **Diffraction**, **Depthmap** and **CMYK
  split** use the whole picture.

**Remove mask** shows and engraves the whole picture again (select the masked picture first).

## Tutorials

- [Mask a picture with a shape](tutorial:pic-clip)
