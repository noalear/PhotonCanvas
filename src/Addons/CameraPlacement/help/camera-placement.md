# Camera placement

This add-on adds two buttons to the **Onto pieces** group of the **Placement** tab: **Cut round prints** finds printed stickers and labels on the bed and cuts round each one, and **Every piece** puts a copy of the selected design on every piece the camera finds. Both need a connected machine and a calibrated camera (see [Camera](camera.md)).

## Every piece

<!-- windows: BatchPlaceWindow -->

**Every piece** puts a copy of the selected design on every piece lying on the bed: it photographs
the whole bed, finds each piece, and puts a copy on its middle, turned to match. For batches:
coasters, tags, keyrings. Texts and codes with fields (serial numbers, spreadsheet rows) mark the
next number or row on each copy. Not yet tried on the machine: in particular, whether the bed's
honeycomb is ever taken for pieces.

Lay the pieces on the bed apart from each other (touching pieces count as one), all the same
thickness, away from the edges of the work area: the bed's colours are read round the edges of the
photo, and whatever differs from them is a piece. Select the design and click **Every piece**.
**Place on every piece** opens; nothing changes until **Apply**.

**1 Height.** **Distance (mm)** (0 to 60): the distance to the top of the pieces, as the machine's
focus uses it. It starts at the machine's current focus. Finding stays off until the camera is
calibrated near this height.

**2 Find.**

| Setting | Range | Default | What it does |
|---|---|---|---|
| **Tolerance** | 3 to 60 | 18 | How different from the bed a colour must be to count as a piece. Raise it if the bed's pattern shows up as pieces; lower it if pale pieces are missed. |
| **Smallest piece (mm²)** | 5 to 20000 | 40 | Specks, scraps and reflections smaller than this are ignored. |
| **Turn each copy to match its piece** | on, off | on | Turns each copy so its long side runs along the piece's long side. A long side has no front or back, so a copy is never turned more than 90°: check which way round it lands. |
| **Snap each copy to its piece's edges (when the design is the piece's outline)** | on, off | off | After placing, fits each copy to its piece's edges (up to 15° more turn, within 8 mm, no resizing), when at least half its outline lands on an edge. |

**Take photo and find pieces** photographs the whole bed (0.4 mm per pixel) and shows each piece's
long axis (an orange line), its copy (green) and its number. Pieces are numbered back to front in
rows, then left to right: the order serial numbers and spreadsheet rows count up.

**Apply** replaces the selected design with the copies, one per piece (each grouped when the design
has several shapes). `Ctrl+Z` undoes it.

## Cut round prints

**Cut round prints** is print-then-cut: lay printed stickers or labels on the bed, and the camera
finds each printed piece of art on the paper and a cut goes round each one, a margin outside it. Not
yet tried on real prints.

1. Lay the printed sheet flat on the bed and focus on it (the picture lines up per height).
2. To look at only part of the bed, select a shape round that part first: the search stays inside
   the selection's bounding box. With nothing selected, the whole bed is searched.
3. Click **Cut round prints** and type the margin: how far outside the printed art the cut goes, in
   mm (default 1; 0 cuts on the art's edge).

How it works:

- **The photo.** When the close-up camera's position is known at this focus height and the area
  lies inside what it sees, a sharp close-up photo is used. Otherwise a new overview photo is taken,
  which needs the camera calibrated.
- **What counts as print.** The paper's white is measured from the photo; anything clearly darker
  than it, or clearly coloured, is print. Works best on white paper, evenly lit.
- **The cuts.** The art is grown by the margin. Parts of one sticker that come within the margin of
  each other get one cut round them all; holes inside a sticker aren't cut; specks smaller than
  10 mm² are skipped.

The cuts go on a cut layer called "Print cut" (made if there isn't one), named Print 1, Print 2 and
so on, and are selected. Set that layer's power for the paper: a kiss cut for stickers (see
[Layer settings](layer-settings.md)). If nothing is found, the status bar says so: check the print is
on white paper, in the camera's view and well lit.

## Tutorials

- [A copy on every piece](tutorial:cam-every-piece)
- [Cut round printed stickers](tutorial:cam-cut-round-prints)
