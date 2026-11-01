# Pictures

Photos, scans and artwork are pictures: grids of pixels, not outlines. A picture on a Picture layer
is engraved dot by dot. The **Picture** tab also turns pictures into other things: outlines to cut
or score, banknote-style engraving lines, colours or rainbow gratings on metal, and depth maps
carved as a relief.

![The Picture tab of the ribbon](images/ribbon-picture.png)

## The Picture tab

The tab's name turns blue when exactly one picture is selected (click it on the work area or in the
Layers panel). Buttons that work on a picture stay grey until then. A picture selected together with
other shapes doesn't count, except for **Image mask**.

| Group | Buttons |
|---|---|
| Add | [Import picture](#import-a-picture) |
| Turn it into | [Trace](#trace), [Colour on metal](#colour-on-metal) |
| Shape | [Shapes to picture](#shapes-to-picture) |
| Edit | [Filters](#filters), [Eraser](#eraser), [Crop](#crop) |

Add-ons add more buttons to the tab while they're on: **Engraving**, **Diffraction** and
**Depthmap** in Turn it into, **Image mask** and **Remove mask** in Shape, **CMYK split** and
**Centre-line trace** in Edit, a **Relief** group (**3D model**, **Relief preview**, **Depth
outlines**) and **Remove background** in **Clean up**. Each has its own page under **Add-ons** in
the contents; turn them on or off in Settings → Add-ons.

**Colour on metal** and **Diffraction** are MOPA-only features. They show by default; to hide or
show them, open **Settings** (the gear at the top right) → **Equipment** and tick or untick
**Colour on metal** and **Diffraction gratings** under **Features to show**. Unticking **Colour on
metal** also hides the **Colour test** and **Measure colours** buttons on the Laser tab. Hiding a
feature only takes it out of the ribbon: nothing is lost. See [The machine and settings](machine.md).

Right-clicking a picture also offers **Trace picture…**, and the add-ons' own items, such as
**Remove picture background…** and **Depthmap with DepthCharge…**.

## Import a picture

**Import picture** (`Ctrl+J`) opens a file chooser; pick one picture or several. Formats: PNG, JPG,
BMP, GIF, WebP and TIFF. **Home → Picture**, **File → Import picture…** and dragging picture files
onto the window do the same. A picture copied in another program can be pasted with `Ctrl+V`: it
lands the same way. To
turn a drawing (SVG, PDF…) into a picture, see **Import as one picture…** in [Files](files.md).

- **Layer:** pictures go on the first Picture layer that is visible and unlocked. If there is none, a
  new Picture layer called **Pictures** is made.
- **Size:** each pixel is 0.1 mm (254 dpi), shrunk if needed so the longer side is at most 100 mm.
- **Place:** centred in the work area; each further picture is 5 mm further right and down. The
  view zooms to the new pictures.
- A file that can't be read is listed in a message; the others are still imported.

The file's pixels are kept in the design as they are. Changing the size, the tone settings or the
layer never alters them; **Filters**, **Crop** and **Eraser** (and add-ons such as Remove background) make a new
picture in its place (`Ctrl+Z` brings the old one back).

## How pictures are engraved

Only Picture layers engrave pictures. A Picture layer is a layer whose mode is **Picture** in its
layer settings (tooltips call it an Image layer). When the job is built, each picture is resampled
onto the machine's dot grid at the layer's **Resolution**, with its tone settings (below) and the
layer's tone correction (if it has one) applied, and **Dithering** turns the greys into dots to burn
or skip: dark areas get many dots, light areas few, white none. The picture's own pixel count doesn't
matter (except with **Pass-through**): the layer's resolution decides how many dots are burned.

- White and see-through parts aren't burned, and nothing outside an image mask is. With **Invert
  (for dark materials)** ticked, white parts count as black and do burn; see-through parts (more
  than half see-through) are never burned, inverted or not.
- A picture on a Cut, Score or Engrave layer isn't engraved, and shapes on a Picture layer aren't
  done. The Layers panel shows a warning under such a layer.
- One picture may make at most 40 million dots. Lower the resolution or make the picture smaller
  if the job says it needs more.
- The layer's **Picture mode** chooses **Dots (dithered)** (every dot at full power, tone from dot
  spacing: proven on the machine), **Greyscale (experimental)** (each dot's power follows the
  picture) or **Depth slices (relief)** (the picture is a depth map, carved down in slices).

Every Picture layer setting (power, speed, resolution, dithering styles, halftone cells, passes,
scan angle, tone correction…) is explained in [Layer settings](layer-settings.md).

![Layer settings of a Picture layer](images/layer-settings-picture.png)

## Size, place and tone

With a picture selected, the **Selection** panel shows its **X**, **Y** (top-left corner), **W**,
**H**, **Scale %** and **Angle °**, and its **Layer**. Type new values to move, resize or turn it
exactly; **Keep proportions** keeps the picture from stretching. **Layer** moves it to another layer
(it must be a Picture layer to be engraved). The panel itself is described in
[The workspace](workspace.md).

Below it, the **Picture** card holds the picture's tone settings. They change how the picture
engraves, not the file, and each picture has its own. Each slider's value shows at its right
("+20%", "1.40 (lighter mid-tones)", "off").

![The Picture card in the Selection panel](images/panel-selection-picture-card.png)

| Setting | Range (default) | What it does |
|---|---|---|
| **Brightness** | −1 to +1 (0) | Lighter (+) or darker (−) all over. |
| **Contrast** | −1 to +1 (0) | More (+) or less (−) difference between light and dark. |
| **Gamma** | 0.2 to 3 (1) | Above 1 lightens the mid-tones, below 1 darkens them; black and white stay. |
| **Sharpen** | 0 to 3 (0) | Brings out edges and fine detail before dithering (an unsharp mask about 0.3 mm wide). About 1 is a good start. |
| **Invert (for dark materials)** | off | Swaps light and dark: what was white burns, what was black doesn't. |

They're applied in this order: invert, gamma, contrast, brightness. **Reset** puts all of them back.
**Trace into vectors…** does the same as the ribbon button; add-ons can put buttons of their own
under it (such as **Remove background…** and **Depthmap…**).

- The work area shows the picture's own pixels, not these settings, unless
  [Pictures as burned](#pictures-as-burned) is on.
- **Engraving**, **Centre-line trace** and **Pictures as burned** use them too, all but
  **Sharpen**. **CMYK split** uses them and copies **Sharpen** onto each plate (see
  CMYK split). **Trace**, **Colour on metal**, **Diffraction** and the filters work
  from the picture's own pixels and ignore them.

## Pictures as burned

**View → Pictures as burned** draws pictures on the work area as they'll come out on their layer's
material: with their tone settings, corrected for the material, in the material's own colours (bare
wood to its darkest burn, for example). See-through parts show as bare material.

It needs a tone test: a picture layer gets its correction from **Laser → Tone test**, or when you
choose a material whose library settings have one. Pictures on layers without one are drawn as
usual. The setting is remembered. See [Materials and tests](materials-and-tests.md).

## Trace
<!-- windows: TraceWindow -->

**Trace** (`Ctrl+T`) turns a picture into vector outlines you can cut, score or fill: logos, signs,
silhouettes, scanned drawings. The window shows the outlines in red over the picture and updates a
moment after each change. The mouse wheel zooms the preview; dragging moves it.

![The Trace picture window in Black & white mode](images/window-picture-trace.png)

**Mode**

- **Black & white:** traces everything darker than **Threshold** (1 to 254, default 128; 0 is black,
  255 white). **Trace the light parts instead** traces the light areas.
- **Colours:** groups the picture's colours and traces each group onto its own layer. **Colour
  tolerance** (1 to 100, default 25) sets how different two colours can be and still count as one;
  **Most colours** (2 to 16, default 8) caps how many groups are kept. Colours covering under 0.5 %
  of the picture join the nearest group. Each group is listed with a tick box, its share of the
  picture and how many outlines it gives; ticked ones are traced. The background (the most common
  colour along the edge) starts unticked.
- **Pick colour (click the picture):** click the preview to pick a colour (clicking switches to
  this mode). **Tolerance** (0 to 100, default 15): low takes only colours very close to the one
  clicked, high spreads to similar ones. **Only the area connected to where I clicked** (on by
  default) keeps just the patch joined to the click; off takes that colour everywhere.

**Shape**

| Setting | Range (default) | What it does |
|---|---|---|
| **Smoothness** | 0 to 10 (2) | 0 follows every pixel step ("exact"); higher gives smoother, simpler outlines with fewer points. |
| **Corners** | 0 to 180° (40°) | Bends sharper than this stay sharp corners; gentler ones become curves. 0 = straight lines only, 180 = all rounded. |
| **Ignore specks** | 0 to 400 px (12) | Blobs and holes smaller than this many pixels are left out. 0 keeps everything. |

The line under the sliders counts the outlines and segments.

**Result**

- **Put on layer** (Black & white and Pick colour): an existing layer that isn't a Picture layer, or
  **New layer** (called "Traced" and the picture's name). It starts on the active layer if that
  can take outlines.
- In **Colours** mode each colour goes on a layer named after it (**Colour #RRGGBB**), shown in that
  colour; a layer of that name is reused if there is one. A colour too dark to see on the work area
  (black, for example) is shown in one of the program's layer colours instead, keeping its name.
- **New layers:** what any new layer does: **Cut**, **Score** or **Engrave (fill)**. You can change
  it later.
- **Remove the picture after tracing** deletes the picture; otherwise it stays under the outlines.
- **Show picture underneath** only affects the preview.

**Add to design** places the outlines exactly over the picture, one shape per colour (holes stay
holes). Pictures larger than 2000 pixels along their longer side are traced from a copy averaged
down to 2000; the outlines are still placed at full size.

## Colour on metal
<!-- windows: ColourPictureWindow -->

**Colour on metal** makes a colour picture on stainless steel or titanium with the MOPA laser, using
the colours your plate really made. Each pixel becomes the nearest measured colour, with dots of
neighbouring colours mixed in for the shades between, and each colour becomes one Engrave layer
with the settings that made it.

It needs a measured palette first: burn a **Colour test** (Laser tab), read it with **Measure
colours** and save the palette. See [Materials and tests](materials-and-tests.md). Until then the
window says there are no palettes and **Add colour layers** stays grey.

![The Colour picture on metal window before any palette is measured](images/window-picture-colour-on-metal.png)

| Setting | Range (default) | What it does |
|---|---|---|
| **Palette** | | A measured palette from the material library: material, palette name and date. |
| **Dot size (mm)** | 0.05 to 3 (0.3) | Size of one colour dot. Smaller = finer, but heat makes neighbouring colours bleed: 0.2–0.5 mm is a good start. |
| **Mix colours with dots for in-between shades** | on | Dithering between palette colours. Off: each dot takes the nearest colour only (posterised). |
| **Fit the picture's light and dark to the palette** | on | Squeezes the picture's lightness into the range the palette really has, so shadows and highlights both land on a colour. |
| **Use bare metal as a colour** | on | Unmarked metal counts as a colour (usually the lightest). If the palette has no bare-metal reading, a light grey stands in. |
| **Merge colours closer than ΔE** | 2 to 20 (5) | Palette colours closer than this count as one: fewer layers, fewer near-duplicates. |
| **Overscan (mm)** | 0 to 2 (0) | Run-up past each line with the laser off: steadier colour right to the edges, a little slower. |

The right half shows the picture and the result simulated in the measured colours. **Colours used**
shows a chip per colour; hover one for the settings that made it and its share of the picture. The
summary gives the dots, colours, layers and the marking time.

**Add colour layers** adds one Engrave layer per colour (named **Colour ·**, the colour and its test
square, e.g. **R2 C5**), least energy first, over the picture at the same place, size and angle.
Bare metal needs no layer. If the picture is alone on its layer, that layer stops being sent; if it
shares it, turn its output off yourself. See-through parts stay bare.

> **Tip:** Use the same plate, finish and focus as the colour test, and the same lights for judging.
> Colours shift with the viewing angle, as on the test.

## Shapes to picture

**Shapes to picture** turns the selected shapes into a picture, for example to engrave a logo with a
picture style (halftone, line screen) or with picture settings. It asks for the **Resolution (dpi)**
(25 to 2540, default 300; other values do nothing).

- Closed shapes are filled black, open lines drawn 0.2 mm wide, the rest see-through.
- The picture, **Shapes as a picture**, lies exactly over the shapes, on the first visible unlocked
  Picture layer or a new **Pictures** layer.
- The shapes stay: delete them or turn their layer's output off if they shouldn't run too.

## Filters
<!-- windows: PictureFilterWindow -->

**Filters** changes the picture itself, with a before-and-after preview. **Apply** replaces the
picture's pixels; `Ctrl+Z` brings the old ones back. Brightness, contrast and sharpening stay in the
picture's own settings ([Size, place and tone](#size-place-and-tone)).

![The Picture filters window with Greyscale chosen](images/window-picture-filters.png)

| Filter | Slider (default) | What it does |
|---|---|---|
| **Greyscale** | none | Colour turned to grey. |
| **Saturation** | **Less ← colour → more**, −1 to +1 (0.4) | Less or more colour. |
| **Pencil sketch** | **Line width**, 0 to 1 (0.4) | Dark lines where the picture changes, white elsewhere. |
| **Emboss** | **Strength**, 0 to 1 (0.3) | A grey relief lit from the top left. |
| **Comic** | **Tones**, 0 to 1 (0.2) | A few flat tones (3 to 8 per colour) with dark outlines. |
| **Black and white** | **Threshold**, 0 to 1 (0.5) | Pure black and white: darker than the threshold goes black. |
| **Mosaic grid** | **Tile size**, 0 to 1 (0.12) | Square tiles (the slider × 100 pixels) with thin white gaps. |

The preview is worked out on a copy at most 900 pixels across; **Apply** filters the full picture.

## Eraser

**Eraser** rubs out parts of a picture: drag over them and they turn white, so they aren't
engraved. Background clutter, a stray mark, a corner.

- The brush is 3 mm across, shown as a circle round the pointer.
- A stroke works on the top picture under where you start it (on a visible, unlocked layer). The
  picture doesn't have to be selected.
- Each stroke is one step to undo (`Ctrl+Z`).
- Erased parts become white and see-through, so they're never burned, even with **Invert (for dark
  materials)** ticked. To clear a whole backdrop at once, use the Remove background add-on.

Press `Esc` or choose another tool to stop erasing.

## Crop
<!-- windows: CropWindow -->

**Crop** cuts the picture down to part of it. An orange box on the picture shows what's kept.

![Crop: the frame on the picture, and its edges as percentages](images/window-picture-crop.png)

| Setting | Range (default) | What it does |
|---|---|---|
| **Shape** | **Free** | Or **Square 1:1**, **4:3**, **3:4**, **3:2**, **2:3**, **16:9**: changing the width sets the height to match (and the other way round), in true pixels. |
| **Left %**, **Top %** | 0 to 99 (0) | Where the kept part starts, as a share of the picture's width and height. |
| **Width %**, **Height %** | 1 to 100 (100) | How much is kept. |

**Crop** keeps the picture's scale: the kept part stays exactly where it was on the work area, and
the picture gets smaller by the part cut away. Its tone settings and image mask are kept.

## Try it

- [Import a picture and set its tone](tutorial:pic-import)
- [Trace a picture into outlines](tutorial:pic-trace)
- [A colour picture on metal](tutorial:pic-colour-on-metal)
- [Turn shapes into a picture](tutorial:pic-shapes-to-picture)
- [Picture filters](tutorial:pic-filters)
- [Rub out parts of a picture](tutorial:pic-eraser)
- [Crop a picture](tutorial:pic-crop)
- [See pictures as they'll burn](tutorial:pic-as-burned)
