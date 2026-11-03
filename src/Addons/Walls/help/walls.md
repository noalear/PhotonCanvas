# Textured walls

An add-on (Settings → Add-ons → **Textured wall maker**): on when PhotonCanvas is installed, and can be turned off. Its button is **Home → Draw → Walls**, and **Edit walls…** is on the right-click menu of the pieces it made.

**Walls** makes walls for tabletop games, dioramas and model buildings, cut from sheet material with
a texture etched on: brick, stone, wood, metal, or a pattern of your own. A wall is built as a
**box** (a textured front and back, a base it stands on, a top, two ends and ribs inside) so it
stands up on its own and looks solid from every side, or as a **flat panel**. A wall can have
battlements (with a walkway behind) or a ruined top, a door, windows and mount points for
accessories. Columns cut from the same sheet join the walls: straight on, at corners, at 45° and
135°, as T junctions and crosses, or closing off an end.

> **Note:** The walls have been checked on screen and in tests, but not yet cut on the machine. Cut
> one short wall and a column first and check how the tabs go into the slots before cutting a whole
> set; change **Slot fit** if they're too tight or too loose.

## The Textured walls window
<!-- windows: WallsWindow -->

![The wall maker making a boxed stone castle wall: settings on the left, and on the right the wall put together with battlements, a walkway, arrow slits, a door and corner columns](images/window-walls-castle.png)

Click **Walls** on the **Home** tab (in the **Draw** group). The **Textured walls** window opens. The
settings are on the left in tabs; the preview on the right updates as you change things:

- **Put together** shows one wall built, in 3D, with a column at each end (the first kind of column
  you're cutting at its left, the second at its right), its door, accessories and walkway in place.
  Drag it to turn it round.
- **Pieces** shows every piece at the same scale, as it will be cut: the wall's front on top, then
  one of each other piece.

Under the preview are the number of pieces and any problems: a red ✕ has to be fixed before the
walls can go in, a yellow ! is worth a look, and a grey • is just for your information.

Press **Put in the design** and the pieces go on a page of their own, nested onto the bed (more pages
if they need more than one bed). To change a set later, select any of its pieces, right-click and
choose **Edit walls…**, change the settings and press **Update in the design**: the old pieces are
replaced wherever they are.

## The wall

**Build it as** picks how each wall is made:

- **A box: front, back, top, bottom and ends** (new walls start like this). The wall is as thick as
  **Wall thickness**, made from panels **Panel thickness** thick: a textured front and back, a base
  plate they stand on, a top plate between them, an end at each end, and ribs inside that hold the
  faces apart. It stands up on its own, its ends can be finished and textured, and doors and
  windows are lined right through (see [Boxed walls](#boxed-walls)).
- **A flat panel**: one sheet thick (or two glued back to back), its ends sliding into the columns'
  slots. This is how the first walls were made, and walls made that way open as they were.

| Setting | Range (default) | What it does |
|---|---|---|
| **Length** | 10 to 2000 mm (100) | How long the wall is: a box wall's faces, from column to column. |
| **Length is** | The panel as cut | *The face as cut*, or *Column centre to centre*: the distance between the middles of the columns at each end. Use centre to centre to lay walls out on a grid (50.8 mm for two 1-inch squares). |
| **Height** | 5 to 1000 mm (50) | How high the wall is from the table, to the top of any battlements. |
| **Wall thickness** (box) | 1 to 200 mm (12) | The whole wall, front face to back face. It must be at least three panels. Two faces plus a whole number of panels between them fits exactly (12 mm, 15 mm or 18 mm for 3 mm sheet); other thicknesses work, and the list says how much room the layers inside leave. |
| **Plinth** (box) | 0 to 20 mm (1.5) | How far the base sticks out past the faces, like a footing course. At 0 the faces' tabs show as notches in the base's edge. |
| **Ribs every** (box) | 15 to 500 mm (45) | The most room between the ribs inside. Ribs keep clear of doors, windows and mount points. |
| **Ends go into columns** (box) | on | The faces have tabs at their ends that go into slots in the columns, and the wall stops at the column's side. Off, the ends are closed: the base goes round them as a plinth, and they can be textured. |
| **Panel thickness** (box), **Thickness** (flat) | 0.5 to 20 mm (3) | Your sheet's thickness. Slots, tabs and mount points are sized from it. |
| **Two panels back to back** (flat) | off | Cuts a mirror-image second panel to glue to the back of each wall, so both faces are textured. The wall is then twice as thick and the slots are made to fit. |
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
| Brick | length 8, height 3.5 mm | Bricks in running bond, each row moved along half a brick: worn edges, knocked corners, the odd spalled or cracked brick. |
| Stone blocks | length 12, course 6 mm | Cut stone in rows of different heights, weathered and chipped. |
| Fieldstone | size 9, course 6 mm | Rough, rounded stones of uneven shapes and sizes. |
| Tiles | 6 × 6 mm | Square tiles or blocks in straight rows, the odd one chipped or cracked. |
| Wood planks (upright) / (across) | width 5, length 35 mm | Boards with butt joints, flowing grain that bends round knots, splits and nail holes. |
| Timber frame | beam 3, bay 20 mm | Tudor style: posts, rails and braces stand out with their grain; the plaster between them is etched. |
| Corrugated metal | pitch 2.5 mm | Upright ridges, dented and scratched, with riveted sheet overlaps. |
| Metal plates | 20 × 14 mm | Riveted plates with the odd patch plate or weld seam, scratches and rust pits. |
| Hexagons | 8 mm | Hexagonal tiles or armour, some with an inset face. |
| Dungeon blocks | length 14, course 8 mm | Big, worn blocks: chipped, spalled, pitted and cracked. |
| Cave rock | rock 16, layer 10 mm | Rough rock in uneven, tilted layers with bedding lines, split by cracks and pitted. |
| Sci-fi panels | 16 × 12 mm | A bulkhead: a kick plate of vents along the foot, bays of panels with bolted ribs between them, and modules of different heights in each bay (inset plates, recessed panels, vent grilles, hatches with hinges and a grip, pipe runs with clamps, clusters of ports and knurled blocks, light strips, display panels and the odd band of hazard stripes). |
| Sandstone | length 16, course 7 mm | Long, soft-cornered blocks eaten by the wind: rounded edges, wind lines and clusters of pits. |
| Adobe | patch 14, brick 3 mm | Smooth mud plaster with ragged patches fallen away to show worn bricks, and spreading cracks. |
| Plain | | No texture: just the outline, doors, windows and mounts. |

- **Groove**: how wide the etched gaps between bricks, stones or planks are.
- **Variation**: stones, blocks and planks are laid out at random; each number gives a different
  layout, and **Shuffle** picks the next one. The same number always gives the same wall.
- **Wear %**: how weathered the texture looks. 0 is new and crisp (straight edges, nothing broken),
  50 is the usual look, 100 is ancient and battered. More wear means more nicked edges (so the gaps
  between stones vary in width), knocked-off corners, bites out of the edges, clusters of pits,
  weathered patches, cracks (the worst etched open, the rest scored), and on metal scratches, dings
  and rust pits. Some stones stay almost smooth and others are badly worn, and how worn the wall is
  drifts slowly along it, as on a real wall. Changing the wear doesn't move any stones: the layout
  comes from **Variation** only.
- **Detail size**: the size of the smallest marks, in mm: pits, nicks, wood grain, bolts, vent slats,
  pipes and greebles. Each texture picks a sensible size when you choose it. Make it bigger for a big
  wall or a coarse material (the marks stay clear), smaller for fine work.
- **Etch as**: *Grooves* etches the gaps as filled areas (deeper and the most real). *Lines* scores
  round each brick or stone instead, which is much quicker. Grain, rivets and door handles are always
  scored lines, on a light **Walls · lines** layer.
- **Keep the ends smooth** (flat walls): leaves the ends of the wall (as far as the slots are deep)
  without texture, so they slide into the columns at full thickness. On by default.
- **Texture the back face too** (box walls): on by default. The back is laid out differently from
  the front, so the two faces don't match stone for stone.
- **Texture the ends and the top too** (box walls): the texture on the top plate (or the walkway),
  on closed ends and on the back of a parapet. The courses on the ends line up with the faces'.

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
  **Merlon heights** can be *All the same height*, *Every other one taller* or *Taller at the ends*
  (like towers at the corners); **Shorter by** is how much lower the shorter merlons are. The wall's
  **Height** is the top of the tall ones.
  **Walkway behind the battlements** adds a strip, **Walkway depth** deep, that sits on brackets
  behind the wall 8 mm below the gaps; the brackets' tabs plug into slots cut through the wall under
  it. The walkway is as long as the wall between the columns.

## Broken tops

![A brick wall with battlements of alternating heights, broken by a breach in the middle, put together with its corner columns](images/window-walls-ruins.png)

**Broken** breaks the top for a ruin, whether it's straight or has battlements, so a castle wall can
have its battlements and a breach through them. On brick, stone blocks, fieldstone and the other
stone textures, the break follows the joints: whole stones are taken away, so the edge runs round the
stones like real fallen masonry. On planks, metal and the like it breaks in a jagged line instead.

| Broken | What it looks like |
|---|---|
| *Crumbled* | Stones fallen out along the top, most of them in one place. |
| *Breach* | A deep gap knocked through the top, with steep sides and crumbled shoulders. |
| *Fallen end* | Standing to full height at one end, falling away towards the other. |
| *Weathered* | Worn down unevenly all along, with a few deeper gaps. |
| *Shattered* | Jagged cracks rather than stones, for concrete, rock or metal. |
| *Stepped* | The first broken top: a dip in stone-sized steps (walls made before keep it). |

**Broken down %** is how far down the break goes, as a share of the height, and **Where** is where
it's deepest (*Anywhere*, *Left*, *Middle* or *Right*); for a fallen end it's which end has fallen.
**Variation** on the Texture tab breaks it another way. On a box wall the front and back break round
the same place, each its own way, and a break is never so deep there's no room for the box under it
(the wall's checks say when it's been made shallower). Behind a walkway only the parapet breaks. The
ends stay up to the top of their tabs into the columns; with a fallen end those tabs are kept low.

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
with a red ✕. In a box wall a door stands on the base (the base runs under it as a threshold), and
every opening is lined between the faces (see [Boxed walls](#boxed-walls)). Bars are left in the
front face; the back face's window is open.

![A timber-framed house wall with an etched door, barred windows and two mount points with torch sconces](images/window-walls-house.png)

## Mount points

**Mount points** are small slots through the wall (through a box wall's front face), as wide as the
sheet is thick (plus the fit) and **Slot height** tall, at **Mount height**. They're spread evenly
along the wall clear of the doors and windows. Anything cut from the same sheet with a tab that size
plugs into them, so one slot takes whatever you want to hang there. A box wall's ribs keep clear of
the mount points, so an accessory's tab has room inside.

**Accessories** cuts one of these for each mount point (or one of each):

- **Wall pegs**: a tab with a shoulder that sticks out of the wall; glue a picture, shield or
  banner to its end.
- **Torch sconces**: a torch in a bracket, standing out from the wall.
- **Shelf brackets**: a triangle that holds a little shelf.
- **Wall lamps (sci-fi)**: a lamp housing that stands out from the wall, its lens scored on.

The accessories stand out from the wall at right angles, so they're drawn side-on.

## Boxed walls

![A boxed stone castle wall put together: battlements over a walkway, a door, arrow slits, and battlemented corner columns on stepped plinths](images/window-walls-castle.png)

A box wall is cut as these pieces (the **Pieces** view shows each one):

| Piece | What it is |
|---|---|
| **Front**, **Back** | The faces, textured. Tabs along the bottom go into the base; tabs at the ends go into the columns. The back is cut as seen from behind. |
| **Base** | What the wall stands on: the faces' footprint plus the plinth, with slots for the faces', ends' and ribs' tabs. It runs under a doorway as a threshold. |
| **Top** | A plate between the faces at the top. Where a window runs through it, it's cut in two. With a walkway, it's the **Walkway**. |
| **End** | Across the gap at each end, from the base to the top plate. |
| **Rib** | The same, inside, every **Ribs every** or less. |
| **Lining** | Layers of sheet between the faces round each door and window, 3 mm wide: from any side the opening looks cut through solid wall. One per layer that fits between the faces. |
| **Merlon filling** | Battlements: the gap between the faces' merlons filled with layers, sitting on the top plate at the bottom of the gaps. |
| **Parapet backing**, **Walkway** | Battlements with a walkway: the front face goes up as the parapet with a second layer behind it, the back face stops under the walkway, and the top plate is the walkway. A walkway deeper than the wall reaches back over the back face on **Walkway brackets** plugged into slots in the back face. |
| **Broken top layer** | A broken top: the front and back break in the same place but each its own way, and the layers between them break a little lower here and there, so the top looks broken right through. |

**Putting it together**: stand the faces' bottom tabs in the base. Glue the ends, the ribs and the
linings to the inside of the front, with the ribs' and ends' tabs in the base too; put the top plate
on, then glue the back on. With battlements, glue the merlon filling or the parapet's backing onto
the top plate.

## Connectors

The columns the walls go into. Type how many of each kind to cut: for a square room, four
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

![A ruined fieldstone box wall between a boxed straight column and a corner column, both with stepped caps](images/window-walls-connectors.png)

### Columns for box walls

A box wall's faces butt against the column's side, their end tabs in its slots (**Slot fit** wider
than the sheet). **Columns are** picks how they're made:

- **Boxes: four sides, a top and a bottom**, for straight runs, corners, T junctions, crosses and
  end caps. Two sides are the column's full width and two fit between them. They go round a plug on
  the base (a square that fits inside them) and another under the top, which line them up. The sides
  are textured like the walls, except where a wall covers them. A boxed column must be at least the
  wall's thickness, two sheets and 2 mm wide (the list says if it's narrower); 22 mm suits a 12 mm wall.
- **Stacks of layers**: each layer has the two slots for each wall. Columns at angles other than a
  right angle (135°, 45° and your angle) are always stacks.

| Setting | Choices (default) | What it does |
|---|---|---|
| **Top** | A wider cap | *Flush with the sides*: a plate inside the top. *A wider cap*: a plate **Lip** wider all round, on top. *A stepped cap*: two plates, the upper one wider, like a cornice. *A stepped point*: plates getting smaller, a stepped pyramid. *Battlements*: the sides go up past the top with a gap in the middle of each, like a turret. |
| **Bottom** | A wider foot | *Flat*: a plate the column's size. *A wider foot*: a plate **Lip** wider all round. *A stepped plinth*: two plates, the lower one wider. Where a wall comes in, the plates are notched so the wall (and its base) reaches the column's side. |
| **Lip** | 2 mm | How far each step of a cap, foot or plinth sticks out. |

### Columns for flat walls

![A flat brick wall with battlements, and one layer and the cap of the corner and 135° columns](images/window-walls-flat.png)

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
