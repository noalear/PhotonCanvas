# Handwriting to font

An add-on (Settings → Add-ons → **Handwriting to font**): on when PhotonCanvas is installed, and can
be turned off. Its button is **Home → Draw → Handwriting**.

Write anything in your own handwriting, as single lines: one pass of the beam down the middle of
each pen stroke, the way you wrote it. That engraves far faster than outlined or filled text, and
looks handwritten because it is. You make the font once from a sheet you fill in and photograph;
after that you type, and it writes.

## Make a font from your handwriting
<!-- windows: HandwritingFontWindow -->

Click **Handwriting**, then **Make a font…** (or search for "Make a handwriting font"). The window
takes you through three steps.

1. **Print the sheet.** Pick **Paper** (**A4** or **US Letter**) and click **Save the sheet as
   PDF…**. Print it at actual size (100%, not "fit to page"). The sheet has a box for each capital,
   small letter, number and common sign (88 in all), and a black square in each corner; the
   top-left square is hollow.
2. **Fill it in and read it back.** Write one character in each box with a dark pen (a black
   felt-tip works best; a ballpoint is fine if it's dark), standing on the solid line: capitals and
   tall letters reach the top line, small letters the dashed line, tails (g, j, p, q, y) hang
   below. Write the letters separately, not joined up. Then:
   - **Open a photo or scan…** reads a phone photo or a scan (JPEG, PNG and the like). The photo
     can be taken at an angle or turned any way round: the four corner squares tell the program
     where everything is. Get the whole sheet in, flat, in even light, with nothing dark touching
     the squares; straight from above is best.
   - **Use the selected picture** reads a picture already in the design (**Picture → Import
     picture**).
   - **Photograph it on the bed** takes an overview camera photo with the sheet lying in the
     machine (only when a laser is connected). Nothing moves and the laser stays off. Phone photos
     are sharper: use them for small handwriting.

   Each box then shows on the right: the photo in grey and, in red, the lines the laser will
   follow. A box with nothing in it says **empty** and is skipped. If something's off:
   - **Ink threshold** sets how dark counts as ink, against the paper around it (35–85, starting
     at 62). Lower it if the printed box lines creep into the letters; raise it if thin or pale
     strokes go missing.
   - **Smoothing** straightens the traced lines out (5–60, about 0.05–0.6 mm on the sheet). More
     gives smoother curves; less keeps small wiggles.
   - Click a letter to leave it out (it greys over and says **left out**); click again to keep it.
3. **Save the font.** Give it a **Font name** and click **Save font**. If a font with that name is
   there already, choose **Add as extra versions of its letters** or **Replace the letters it
   has**.

> **Tip:** Fill in a second sheet and read it into the same font with **Add as extra versions of
> its letters**. Each letter then has two versions, and when a letter comes up twice the writing
> switches between them, so words look less stamped.

How a box is read: the photo is straightened from the corner squares and each box sampled at 10
pixels per mm. Pen strokes are thinned to their centre lines and followed end to end; pieces that
meet straight on are joined back into one stroke, as the pen drew them. Specks under 0.15 mm² are
dropped. Small blobs (dots on i and j, full stops) become a small closed ring. Each letter is kept
at the height you wrote it relative to the solid line, so tails and small letters sit right.

## Write with it
<!-- windows: HandwritingWindow -->

**Home → Draw → Handwriting** opens the writing window. Pick the font, type the **Words** (Enter
starts a new line) and watch the preview.

| Setting | What it does |
|---|---|
| **Capital height (mm)** | How tall capital letters come out (1–300 mm). Everything else keeps its size next to your capitals, as you wrote it. |
| **Letters apart (%)** | Space between letters, as a share of the capital height (−50 to 100, starting at 12). |
| **Words apart (%)** | The width of a space (10–300, starting at 50). |
| **Line spacing (%)** | Baseline to baseline (80–500, starting at 190). |
| **Slant (°)** | Leans the letters: + to the right, − to the left (−30 to 30). 0 keeps them as you wrote them. |
| **Align** | **Left**, **Centre** or **Right**, for several lines. |
| **Natural variation** | 0 writes every letter exactly as on your sheet. More makes each letter a little bigger or smaller, turned or lifted, so words look less regular. |
| **Another variation** | The same words with a different wobble (and different versions of repeated letters). The result is the same every time you open it. |

Under the settings, the window says how many letters it wrote and how big, and which characters
the font doesn't have (left as a gap). Some are written with a stand-in: an accented letter without
its accent (é as e), curly quotes as straight ones, dashes as a hyphen, and a letter missing in one
case with the other case.

**Add to the design** puts the writing in the middle of the work area, as one shape on the active
layer if that's a Score layer, else the first visible, unlocked Score layer, else a new Score layer
called **Handwriting**. Score it with light, fast line settings, like any single-line work.

To change the words later, select the writing and click **Handwriting** again (or right-click it →
**Change the handwriting…**): the window opens with its words and settings, and **Change the
writing** updates it in place. The words and settings are kept in the design file; the lines stay
even on a computer without the font, but changing them needs the font.

### Fonts

- **Make a font…**: the window above.
- **Add a font file…**: a handwriting font saved on another computer (a `.json` file).
- **Export…** → **Font file, to use on another computer…** saves the font as one `.json` file;
  **SVG font, for other programs…** saves it as an SVG stroke font, which other single-line tools
  read (Inkscape's Hershey Text extension takes it as a custom font).
- **Delete font**: writing already in designs stays as it is.

Fonts are kept in the add-on's data folder, so they stay when the add-on is turned off, updated or
removed.

## Tutorials

- [Make a font from your handwriting](tutorial:handwriting-font)
- [Write in your handwriting](tutorial:handwriting-write)
