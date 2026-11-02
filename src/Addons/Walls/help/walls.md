# Textured walls

An add-on (Settings → Add-ons → **Textured wall maker**): on when PhotonCanvas is installed, and can be turned off. Its button is **Home → Draw → Walls**, and **Edit walls…** is on the right-click menu of the pieces it made.

**Walls** makes wall panels for tabletop games, dioramas and model buildings, cut from sheet
material with a texture etched on the face: brick, stone, wood, metal, or a pattern of your own. A
wall can have battlements (with a walkway behind) or a ruined top, a door, windows and mount points
for accessories. Columns cut from the same sheet join the walls: straight on, at corners, at 45° and
135°, as T junctions and crosses, or closing off an end. The walls slide into the columns' slots,
so you can take a layout apart and build a different one.

> **Note:** The walls have been checked on screen and in tests, but not yet cut on the machine. Cut
> one short wall and a column first and check how the wall slides into the slot before cutting a
> whole set; change **Slot fit** if it's too tight or too loose.

## The Textured walls window
<!-- windows: WallsWindow -->

![The wall maker making a castle wall: settings on the left, every piece on the right with the brick texture, battlements, arrow slits, a door and its walkway](images/window-walls-castle.png)

Click **Walls** on the **Home** tab (in the **Draw** group). The **Textured walls** window opens. The
settings are on the left in tabs; the preview on the right shows every piece at the same scale (the
wall's face on top, then one of each other piece) and updates as you change things. Under it are the
number of pieces and any problems: a red ✕ has to be fixed before the walls can go in, a yellow !
is worth a look, and a grey • is just for your information.

Press **Put in the design** and the pieces go on a page of their own, nested onto the bed (more pages
if they need more than one bed). To change a set later, select any of its pieces, right-click and
choose **Edit walls…**, change the settings and press **Update in the design**: the old pieces are
replaced wherever they are.

## The wall

| Setting | Range (default) | What it does |
|---|---|---|
| **Length** | 10 to 2000 mm (100) | How long the wall is. |
| **Length is** | The panel as cut | *The panel as cut*, or *Column centre to centre*: the distance between the middles of the columns at each end. Use centre to centre to lay walls out on a grid (50.8 mm for two 1-inch squares). |
| **Height** | 5 to 1000 mm (50) | How high the wall is, to the top of any battlements. |
| **Thickness** | 0.5 to 20 mm (3) | Your sheet's thickness. The columns' slots and the mount points are sized from it. |
| **Two panels back to back** | off | Cuts a mirror-image second panel to glue to the back of each wall, so both faces are textured. The wall is then twice as thick and the slots are made to fit. |
| **How many walls** | 1 to 100 (1) | How many walls like this one to cut. |
| **Cut with** | As set on the cut layer | Cut settings from your material library. Their kerf goes on the cut layer, so the slots come out the width they're drawn, and the material's thickness and slot fit from your cut test are filled in. |
| **Etch with** | As set on the etch layer | Engrave (fill) settings from your library for the texture. |

## Themes

**Theme**, at the top of the **Wall** tab, is a look to start from. Picking one sets the texture and
its sizes, the door's shape and look, the windows' shape, size and bars, the frame round them, the
accessories and the columns' shape. The size, the top, how many walls and the fit stay as they are,
and anything can be changed afterwards. Every terrain maker has the same themes (floor tiles, roofs
and towers, floors and stairs, ruins, bridges, scatter, furniture, the game accessories and whole
buildings), so pieces made in one theme go together on the table.

| Theme | Texture | Door | Windows | Accessories, columns |
|---|---|---|---|---|
| Medieval | Brick | Round arch, planks | Round arch | As set |
| Dungeon | Dungeon blocks | Round arch, iron studs | Small and square, upright bars | Torch sconces, square columns |
| Cave | Cave rock | A rough cave mouth | Rough holes, no frame | Torch sconces, round columns |
| Sci-fi | Sci-fi panels | Angled corners, a metal blast door | Wide, angled corners | Wall lamps |
| Desert | Sandstone | Square top, planks | Small and square, a wide frame | Shelf brackets, square columns |
| Gothic | Fine stone blocks | Pointed arch, iron studs | Tall pointed arches with a cross | Torch sconces, octagonal columns |

Accessories only change when some are being cut: *None* stays *None*.

## Textures

Pick one in **Texture**. The sizes it asks for change with it; the defaults suit 28 mm figures.

| Texture | Sizes (defaults) | What it looks like |
|---|---|---|
| Brick | length 8, height 3.5 mm | Bricks in running bond, each row moved along half a brick. |
| Stone blocks | length 12, course 6 mm | Cut stone in rows of different heights. |
| Fieldstone | size 9, course 6 mm | Rough, rounded stones of uneven shapes. |
| Tiles | 6 × 6 mm | Square tiles or blocks in straight rows. |
| Wood planks (upright) / (across) | width 5, length 35 mm | Boards with butt joints, grain and nail holes. |
| Timber frame | beam 3, bay 20 mm | Tudor style: posts, rails and braces stand out and the plaster between them is etched. |
| Corrugated metal | pitch 2.5 mm | Upright ridges. |
| Metal plates | 20 × 14 mm | Riveted plates. |
| Hexagons | 8 mm | Hexagonal tiles or armour. |
| Dungeon blocks | length 14, course 8 mm | Big, worn blocks with chipped corners and the odd crack. |
| Cave rock | rock 16, layer 10 mm | Rough rock in uneven layers, split by cracks and pitted. |
| Sci-fi panels | 16 × 12 mm | Metal panels of different widths with cut corners; some have vents, bolts, an inset edge or hazard stripes. |
| Sandstone | length 16, course 7 mm | Long, soft-cornered blocks with wind-worn lines and pits. |
| Adobe | patch 14, brick 3 mm | Smooth mud plaster with patches fallen away to show the bricks, and a few cracks. |
| Plain | | No texture: just the outline, doors, windows and mounts. |

- **Groove**: how wide the etched gaps between bricks, stones or planks are.
- **Variation**: stones, blocks and planks are laid out at random; each number gives a different
  layout, and **Shuffle** picks the next one. The same number always gives the same wall.
- **Etch as**: *Grooves* etches the gaps as filled areas (deeper and the most real). *Lines* scores
  round each brick or stone instead, which is much quicker. Grain, rivets and door handles are always
  scored lines, on a light **Walls · lines** layer.
- **Keep the ends smooth**: leaves the ends of the wall (as far as the slots are deep) without
  texture, so they slide into the columns at full thickness. On by default.

### Your own texture

Make a texture from shapes you've drawn or from an SVG drawing. One copy of the shapes is a tile;
the tiles are repeated in rows over the face. Closed shapes are etched as areas, open ones are
scored as lines.

1. Draw the tile (or open the SVG you want). For shapes on the canvas, select them before opening
   **Walls**.
2. On the **Texture** tab, type a **Name**, and set **Gap** (space between tiles) and **Row shift %**
   (50 moves every other row along half a tile, like bricks).
3. Press **From the selected shapes** or **From an SVG file…**. The texture joins the list, marked
   "(yours)", and stays there next time. **Tile width** scales it.

**Delete this texture** takes one of yours off the list. Walls you already made with it keep it:
your texture is saved in the design too, so it can be opened and changed on another computer.

## The top

- **Straight**: a flat top.
- **Battlements (parapet)**: gaps along the top. **Merlon width** is the raised blocks, **Gap width**
  the gaps between them and **Merlon height** how deep the gaps go. The ends stay full height.
  **Walkway behind the battlements** adds a strip, **Walkway depth** deep, that sits on brackets
  behind the wall 8 mm below the gaps; the brackets' tabs plug into slots cut through the wall under
  it. The walkway is as long as the wall between the columns.
- **Ruined**: a broken top, stepped like fallen stones, deepest somewhere near the middle; the ends
  stay whole. **Broken down %** is how far down it goes. **Variation** changes where it breaks.

## Doors and windows

| Setting | What it does |
|---|---|
| **Door** | *Doorway* cuts it through. *Closed door* etches a door on (planks, braces and a ring handle) instead. *Doorway and a door to fit it* cuts the doorway and a loose door 0.3 mm smaller all round with the door etched on: glue it open or shut, or hinge it with a strip of tape or cloth. |
| **Door shape** | Square top, round arch, pointed (Gothic) arch, angled corners (sci-fi), or rough: a ragged cave mouth, different for every **Variation**. |
| **Door look** | What's on the door: *Planks* (braces and a ring handle), *Planks with iron studs* (iron bands and studs), *Metal blast door* (a seam down the middle, hazard chevrons and panels), *Bars (a portcullis)* or *Stone slab*. A loose door with bars has the gaps between them cut through, and is called a portcullis. |
| **Door width**, **Door height** | Its size (mm). |
| **Door position** | Where its middle is, in mm from the middle of the wall: less than 0 moves it left. |
| **Windows** | How many. They're spread evenly along the wall, clear of the door and the ends. If they don't all fit, the list under the preview says how many did. |
| **Window shape** | Square top, round arch, pointed arch, round, an arrow slit (a narrow slit with a cross slit; type a small width, such as 2 mm), angled corners (sci-fi) or rough (cave). |
| **Sill height** | How high the bottom of the windows is. |
| **Bars**, **Bar width** | Material left across the window: a cross, or upright bars. Keep bars 1.5 mm or wider in plywood. |
| **Frame** | A smooth band round each door and window, edged with a groove, where the texture stops. 0 = none. |

A door or window that would cut the wall into pieces, or that doesn't fit its height, is refused
with a red ✕.

![A timber-framed house wall with an etched door, barred windows and two mount points with torch sconces](images/window-walls-house.png)

## Mount points

**Mount points** are small slots right through the wall, as wide as the sheet is thick (plus the
fit) and **Slot height** tall, at **Mount height**. They're spread evenly along the wall clear of
the doors and windows. Anything cut from the same sheet with a tab that size plugs into them from
either side of the wall, so one slot takes whatever you want to hang there.

**Accessories** cuts one of these for each mount point (or one of each):

- **Wall pegs**: a tab with a shoulder that sticks out of the wall; glue a picture, shield or
  banner to its end.
- **Torch sconces**: a torch in a bracket, standing out from the wall.
- **Shelf brackets**: a triangle that holds a little shelf.
- **Wall lamps (sci-fi)**: a lamp housing that stands out from the wall, its lens scored on.

The accessories stand out from the wall at right angles, so they're drawn side-on.

## Connectors

The columns the walls slide into. Type how many of each kind to cut: for a square room, four
**Corner (90°)**; for a long straight run, **Straight (180°)** between the walls and an **End cap** at
each end.

| Kind | Slots |
|---|---|
| **Straight (180°)** | Two, opposite each other: the walls carry on in a line. |
| **Corner (90°)** | Two at right angles. |
| **Wide (135°)** | Two, 135° apart: the wall turns by 45° (for octagonal towers and diagonals). |
| **Sharp (45°)** | Two, 45° apart: a sharp point. |
| **T junction** | Three: a straight run with a wall off one side. |
| **Cross (+)** | Four. |
| **End cap** | One: finishes the end of a wall. |
| **Your angle** | Two, **Your angle (°)** apart. |

![A ruined fieldstone wall and one layer and the cap of each kind of column: straight, corner, 135°, T and end](images/window-walls-connectors.png)

Each column is a stack of layers cut from the same sheet: as many as it takes to reach the top of
the wall (the list under the preview says how many). Each layer has a slot for every wall, as wide
as the wall plus **Slot fit**, **Slot depth** deep, and a **Pin hole Ø** up the middle: push a
cocktail stick or a piece of wire through the stack to line the layers up while the glue sets.
**A wider cap on top** adds a layer 2 mm bigger all round that sits over the walls; **A wider foot
at the bottom** makes the bottom layer 2 mm bigger all round.

- **Column shape**: *Auto* makes square columns for straight runs, corners, T junctions and crosses,
  octagons for 45° steps and round ones for other angles, so each wall meets a flat side.
- **Column width**: across the flats.
- If the slots would run into each other or the pin hole, or leave the column too thin, the list
  says so: make the column wider or the slots shallower.

## Layers and order

The pieces go on three layers, run in this order: **Walls · lines** (light scoring) and **Walls ·
etch** (the texture, a Fill layer) first, while the sheet is whole, then **Walls · cut**. Set the
etch layer's power and speed for the depth you want (Laser → Tests → Material test helps); a deeper
etch shows more but takes longer.

## Try it

- [Make a brick wall with a door and columns](tutorial:draw-walls)
