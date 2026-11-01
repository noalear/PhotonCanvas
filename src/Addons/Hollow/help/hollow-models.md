# Hollow stacked models

**Hollow it out** is an add-on for [stacked slices](3d-slices.md). It keeps a shell round the
outside of each slice and cuts the middle out, so a stacked model takes less material and less
glue and comes out lighter. It can also leave a channel up through the base for a wire or an LED,
and pockets in the base for magnets.

It works only with **Build as: Stacked layers (solid)**. Turn the add-on off (or remove it) and the
slicer makes solid slices exactly as before.

> **Note:** Hollow slices haven't been tried on the machine yet. Cut a small model in scrap first.

## Hollow a model
<!-- windows: StackSliceWindow -->

1. Open a model in **3D to slices** (Home tab) and choose **Stacked layers (solid)**.
2. Tick **Hollow it out** in the stacked settings.
3. Set **Wall (mm)**. The preview shows the hollow slices and the summary says how much material
   it saves.

How it works: each slice's outline is shrunk by the wall's thickness, and only the part of it that
is also that far inside the slices above and below (as many as the wall is thick) is cut out. So
the hollow keeps a wall's thickness from the surface in every direction, and the top and bottom
stay closed: with 3 mm material and a 5 mm wall, the bottom two and top two slices are solid.
Hollows smaller than the wall squared are not worth cutting and are left solid.

The middles that come out are whole pieces of material: keep them for small parts.

| Setting | Range (default) | What it does |
|---|---|---|
| **Hollow it out** | off | Turns hollowing on. |
| **Wall (mm)** | 1 to 50 mm (5) | How thick the shell is, sideways and at the top and bottom. Thicker is stronger and leaves room for the pins. |
| **Open bottom** | off | The hollow runs out through the base: for a lamp shade, or a cover that goes over a light. |
| **Open top** | off | The hollow runs out through the top: a pot or a bowl. |
| **Wire / LED channel Ø** | 0 to 30 mm (0) | A round hole straight up through the solid base into the hollow, at the roomiest spot, for a wire or to push an LED in. Only where there's a straight way up through solid (a model standing on legs may have none). 0 = none. |
| **Magnet Ø** | 0 to 40 mm (0) | Round pockets in the base for magnets, so the model sticks to a fridge or a steel base. 0 = none. |
| **Magnet thickness (mm)** | 0.5 to 20 mm (3) | How thick the magnets are: the pockets go up through as many slices as that takes, with solid material above them. |
| **Magnets** | 1 to 8 (2) | How many pockets, spread as far apart as the base allows. |

## Pins, dowels and labels

- The hollow is cut before the pins are placed, so the **Pin holes Ø** pins go in the walls where
  the slices overlap. A wall too thin for a pin (about the pin plus 2.5 mm) gets fewer pins: make the
  wall thicker.
- **Dowels Ø** holes keep solid material round them, half a wall thick, all the way up. Where that
  leaves a column standing in the hollow, a strip as wide as the wall joins it to the shell.
- If cutting the middle out would leave a part in two pieces (a slice that was already a ring), a
  strip across the hollow keeps it in one piece. The summary counts these bridges.
- Labels and the next slice's marks go on the shell, where there's material.

## The summary

The summary adds a line like "Hollowed with a 5 mm wall: 12 slices have the middle cut out, saving
37 % of the material", and says where the channel and magnet pockets went, or why they couldn't.

- **Too small to hollow**: no slice has room for a hollow with that wall. Make the wall thinner or
  the model bigger.
- **No room for the channel**: there's no straight way up through solid material from the base to
  the hollow.
- **No room for magnet pockets**: the base has no spot clear of its edges, the dowels and the
  channel that's wide enough.

## Putting it together

Glue up as usual, slice by slice. Glue the magnets into their pockets before the slice above them
goes on, and thread any wire up the channel before the top closes the hollow.

## Try it

- [Hollow a stacked model](tutorial:3d-hollow)
- [3D models and stacked slices](3d-slices.md)
