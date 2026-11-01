# See it before you burn it
<!-- windows: BurnPreviewWindow -->

**See it burned** on the **View** tab shows your design as the finished piece: drawn on the camera's picture of
the material that's really on the bed, each layer as dark and in the colour your own test grids say
it comes out. Use it before **Ship It** to catch a fill that will be too pale, a colour that isn't
the one you wanted, or a cut that lands on a knot in the wood. Nothing is sent to the machine and
nothing moves.

This is an add-on: turn it off or on in **Settings → Add-ons**.

## The window

The picture on the left is the design and a few millimetres round it, burned. It follows your
design while the window is open: change a layer's power and the picture changes a moment later.

- **Hold to see before**: hold the button down to see the material as it is now; let go to see it
  burned again.
- **New camera photo**: takes a fresh overview picture of the bed (the camera only: the head stays
  where it is and the laser stays off). Needs the machine connected. Without a camera picture the
  piece is drawn on a plain stand-in in the colour of the material.
- **Show on the canvas**: the same as **Burned on canvas** on the ribbon (below).
- **Layers**: each layer that's sent, with a swatch of its mark on the bare material, and where its
  look came from (next section).

## Where each layer's look comes from

Each layer is drawn from the best thing there is, in this order:

| Shown as | From | When |
|---|---|---|
| ✓ measured | A square of a **Colour test** with the layer's own settings | Fill and score layers whose material (from the library) has a measured colour palette |
| ✓ measured | The layer's **Tone test** | Picture layers, and fill layers, with a tone test |
| ≈ nearest test | The colour-test square whose settings are nearest (within about 15 % all told) | Settings close to, but not the same as, a tested square |
| ? guess | The energy the settings put in, and the kind of material | Anything without a test |

A guess says so, in amber. To turn a guess into a measured look, run the test it needs on a scrap of
the same material ([Materials and tests](materials-and-tests.md)) and choose that material for the
layer.

The mark is laid on the camera's picture as the change from bare material to mark that the test
measured, not as a flat colour, so the grain, the lighting and the edges of the real piece still
show through. Cuts that go right through show the dark bed beneath, with a little scorch either side
on wood, paper and leather. Lines thinner than a pixel of the camera picture come out faint, as they
would in a photo.

## Burned on canvas

**Burned on canvas** on the **View** tab draws the burned look on the work area in place of the camera picture,
under your outlines, so you can zoom in on it and keep editing. Turn **Camera on work area** on to
see it on the real material. A label in the bottom corner says it's on, and whether any layer is a
guess. Click the button again to see the camera picture as it is.

## Good to know

- The camera picture is only true at the height it's taken for: focus on the material first
  ([Camera](camera.md)).
- Colour tests are only good for the material, finish and focus they were made on. A different
  sheet of "the same" steel can give other colours.
- The guesses use the F2 Ultra's 40 W diode and 60 W fiber, and assume wood when no material is
  chosen for the layer.
