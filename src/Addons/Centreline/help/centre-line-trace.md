# Centre-line trace

An add-on (Settings → Add-ons → **Centre-line trace**): on when PhotonCanvas is installed, and can be turned off. Its button is **Picture → Trace → Centre-line trace**.

## Centre-line trace

**Centre-line trace** draws one line down the middle of each dark stroke, instead of an outline round
both sides: handwriting, signatures, line art, single-stroke text. It has no window.

- Dark means darker than mid-grey after the picture's tone settings; see-through parts are ignored.
- The strokes are thinned to one pixel and followed end to end; lines shorter than 3 pixels are
  dropped.
- The lines go on the first visible, unlocked Score layer, or a new Score layer called
  **Centre lines**, as one shape ("Centre lines" and the picture's name). The status bar says how
  many lines it found, or "No dark strokes found to trace."
- The picture stays; turn its layer's output off if it shouldn't be engraved as well.

## Tutorials

- [Centre-line trace](tutorial:pic-centreline)
