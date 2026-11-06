# City builder

An add-on (Settings → Add-ons → **City builder**): on when PhotonCanvas is installed, and can be
turned off. It needs the **Building editor** and **Map to terrain** add-ons (and the makers they use, the
**Mini furniture maker** among them), the **Floor tile maker**, the **Miniature bridge maker** and the
**Scatter terrain maker**: every building on the map is a building editor building, the battle map
reader finds a picture's grid the way Map to terrain does, and the ground, bridges, walls, trees and
scenery are cut by the others. Its button is **Home → Draw → City**.

**City** lays out a whole town, village or city on one big map: the ground of every square (grass,
roads, paving, water, fields…), the buildings standing on it, fences, hedges and town walls along the
grid lines, and trees, rocks, wells and carts. Start from an empty map, from the sample village, or
from a 2D battle map picture: the city builder reads it as a best guess of the town seen from above
and you put right whatever it got wrong. Each building opens in the [building editor](houseeditor.md)
to change its rooms, doors, furniture and roof, and comes out as its laser-cut pieces.

> **Note:** The 3D view is a light one on purpose: plain blocks and flat colours, no textures and no
> engraving depth, so even a big town turns round quickly. The textures are on each building's own
> pieces (and in the building editor's 3D view).

## The City builder window
<!-- windows: CityWindow -->

![The city builder with the sample village on the plan: roads meeting at a paved square, a pond, a field, houses with their roofs, fences, a hedge and trees](images/window-citybuilder-plan.png)

Click **City** on the **Home** tab (in the **Draw** group). The **City builder** window opens with
the last map you worked on (the first time, a small sample village). Along the top are the tools; on
the left are six tabs: **Map** (its name, look and size, your saved maps, saving it as a picture and
making a town for you), **Battle map** (reading a picture), **Tools** (what the tools put down),
**Town** (every building, to find one), **Picked** (what's picked on the plan) and **Cut** (the
ground, bridges and scenery to cut). On the right, **Plan** is the map from above and **3D** shows
it put together.

Under them it says how big the map is on the table, how many buildings and trees there are, and
whether any buildings stand on the same squares (they show red on the plan). The map is saved as you
go and opens again next time.

### Moving round the plan

Scroll to zoom in on what's under the pointer; drag with the middle mouse button (or hold Shift and
drag) to move round; press `F` to see the whole map again. The grid shows once you're zoomed in far
enough, and each building's name once it's big enough to read.

## The tools

| Tool | What it does |
|---|---|
| **Select** | Click a building, a tree or thing, a label or a fence to pick it (its settings show on the **Picked** tab); Ctrl and click picks more, or takes one out again. Drag a box from an empty spot to pick everything inside it. Drag anything picked to move it all, a whole square at a time (the mouse wheel turns it while you drag). See [Picking several things](#picking-several-things) for the keys. |
| **Building** | Drag out a rectangle: a new building stands on it, with walls round it, a door towards the nearest road or paving, windows along the walls and a roof (as many floors as **Floors** on the **Tools** tab says). With the right button, click a building to take it away. |
| **Place** | Put down a saved building (pick it on the **Tools** tab first): click where its top left corner goes. `R`, the right button or Ctrl + the mouse wheel turns it before you click. |
| **Paint** | Paint the ground with the **Ground** picked on the **Tools** tab, as many squares across as **Brush (squares)**. The right button paints grass. |
| **Fill** | Click a patch of one ground to fill it all with another (the right button fills it with grass). |
| **Road** | Drag from one square to another to lay a road there, round one corner; hold Ctrl for a straight line however it slants. What it lays (cobbles, a dirt track, paving, sand, water for a river or grass to take one up) and how many squares wide are under **Roads** on the **Tools** tab. The right button lays grass. |
| **Fence** | Drag along the grid lines to put up the **Fence** picked on the **Tools** tab: a fence, a hedge, a stone wall or a town wall. Start on one that's there to take it away instead. |
| **Trees** | Click to put down the **Tree or thing** picked on the **Tools** tab: a tree, a pine tree, a bush, a boulder, a well, a cart, crates and barrels, a lamp or a statue, or one of the town's bigger pieces: a horse trough, a players' stage (three squares by two), a market stall, an outdoor bar, a notice board, a fountain (two squares by two), a gallows or a guard tower (two squares by two: put it on a grid point of a town wall and it stands astride the wall). The right button takes them away. |
| **Label** | Click to write the **Text** under **Labels** on the **Tools** tab there: a street's name, a district's, a number for your game master's key. The right button takes one away. |
| **Erase** | Drag over labels, trees, fences and buildings to take them away. |

**Undo** and **Redo** (`Ctrl+Z`, `Ctrl+Y`) go back and forward a step at a time.

### Picking several things

With **Select**, everything picked shows orange, and these keys work on all of it:

| Key | What it does |
|---|---|
| `R` (Shift+`R`) | Turn it a quarter turn about its middle, clockwise (the other way). Four turns bring it back where it was. |
| Arrow keys | Move it a square. |
| `Delete` | Take it away. |
| `Ctrl+C`, `Ctrl+X`, `Ctrl+V` | Copy it, cut it, and put a copy down with its top left corner on the square under the pointer. |
| `Ctrl+D` | Put a copy beside it. |
| `Ctrl+A` | Pick everything on the map. |
| `Esc` | Pick nothing. |

![A box dragged round the tavern and the things on the square: all of it picked, orange on the plan, with the type and district for the building on the Picked tab](images/window-citybuilder-several.png)

## The map

On the **Map** tab:

- **Name**: the town's name.
- **Look**: the theme new buildings are made in (medieval, dungeon, cave, sci-fi, desert or
  gothic): their walls' texture, doors, windows and roofs.
- **Squares across** and **Squares down**, then **Make it this size**: the map's size, up to 300
  squares each way. Squares added at the right and bottom are grass; buildings that would hang off
  the edge are taken away.
- **Square (mm)**: one square on the table. 25.4 mm (an inch) is the usual battle map square for
  28–32 mm figures.
- **New map** starts an empty one (all grass, the size above), **Open…** and **Save as…** open and
  save maps as files (once saved, changes go on being saved to the file), and **Sample village**
  brings back the sample.

## Making a town for you

![A walled town made by the city builder: main roads meeting at a market square with its temple and hall, lanes lined with houses, a river with bridges, gates in the wall, towers at its corners and guard towers along it, and fields outside](images/window-citybuilder-made.png)

On the **Map** tab, **Make a town for me** makes a whole town the size of **Squares across** and
**Squares down** (up to 300 each way, in a few seconds), in the map's **Look**:

- **Streets**: how its roads run. **Crossroads**: two main roads crossing, with lanes off them in
  each quarter. **Main street**: one wide street through the town with lanes off it (and back streets
  in a big town). **Grid of streets**: streets in a grid, the main two wider. **Winding road**: a main
  road wandering across, and lanes wandering along beside it. **Riverside**: a river along the
  bottom with a paved quay, a main road along the town, lanes down to the water and back streets.
- **How full**: further left, a house here and there; further right, buildings packed side by side.
- **A town wall with gates**: a town wall round it, with a gate wherever a road goes through, a tower
  in each corner, guard towers astride the wall every dozen squares or so, guardhouses by the gates and
  farms with fields outside.
- **A river through it**: a river down one side, a bridge wherever a road crosses it, warehouses
  along it.
- **A market square**: a paved square where the main roads meet, with a temple, a town hall and a
  tavern facing it, and on it a fountain in the middle (a well on a small square), a players' stage,
  market stalls, an outdoor bar, a notice board, a horse trough, a well, carts, crates, a statue and
  lamps, clear of the roads through it. A dungeon or gothic town's square has a gallows too.
- **Use my saved buildings where they suit**: a plot gets one of your saved buildings when its
  district (and type) suits, turned to face the road.

Press **Make a town** for the town; press it again for another one (each is different), and undo
puts your map back. Every building faces its road with its door, and is of the right kind for its part
of town, which is its **District**: shops, workshops and taverns in the market, houses round it, a
noble quarter of manors, warehouses at the docks, hovels at the town's edge, barns and stables on the
farms. Each has a name (*The Golden Goose*, *Ashford Manor*, *Baker's shop*), the main streets are
named, and there are lamps along them, trees in the gardens and woods outside. Under the button it
says what was made. Then change anything you like: it's an ordinary map.

![The same town in 3D: the wall with its corner towers and guard towers, the houses with their roofs, the bridges over the river and the woods outside](images/window-citybuilder-made-3d.png)

![Closer in on the market square in 3D: market stalls under their awnings, the outdoor bar with its barrels, a notice board, carts and the players' stage with its backdrop round the tiered fountain in the middle, the houses close round it](images/window-citybuilder-square-3d.png)

### Filling the roads with buildings

**Fill with buildings** puts buildings along the roads (and round paved squares) on the free grass,
facing the road, as full as **How full** says, of the kinds for the **District** picked; your saved
buildings where they suit, if that's ticked. With **Select**, drag a box first to fill only inside it
(it stays dashed on the plan until you click again); otherwise it fills the whole map. Lay out roads
with **Road**, then fill each part of town with its own district.

## Saving it as a battle map picture

![A part of a town saved as a battle map picture: cobbled streets round a paved market square with its stage, market stalls, a fountain in the middle, a well, trough, carts and lamps, tiled, thatched and slate roofs lit from one side, the river with its ripples, and the street names](images/citybuilder-picture.png)

On the **Map** tab, **Save as a picture…** saves the map drawn from above as a battle map, for a
virtual table or to print: textured ground (cobbles, flagstones, grass, water with its banks, fields),
every roof with its tiles, thatch or slates lit from one side, shadows, trees, wells and carts,
fences, hedges and town walls, and the grid. Pick PNG, JPEG or WebP when you save it.

- **Pixels per square**: 70 to 140 suits most virtual tables. To print it, 100 pixels to a square
  printed an inch across is 100 dots to the inch. A very big map is drawn at fewer pixels to a square
  (at most 16,000 pixels each way); it says so under the settings.
- **The grid**, **Street names and labels**, **Building names**: what's drawn over it.
- **Only the last box dragged with Select**: just that part of the map (drag a box with **Select**
  first), say one street for tonight's game.

## Reading a battle map
<!-- windows: CityWindow -->

![The sample battle map read into a town: the river, the cobbled roads and paved square, every house with its roof, and the wood as trees, over the picture](images/window-citybuilder-battlemap.png)

On the **Battle map** tab, press **Open a picture…** and pick a top-down map of a town (PNG, JPEG,
WebP…), or **Use the selected picture** for one already in the design, or **Try a sample battle
map** to see how it works. The picture shows under the plan and its grid is found by itself (the thin
lines that repeat evenly right across the picture, dark or light, to a fraction of a pixel; the heavy
outlines of the buildings don't pass for a coarser grid): check **Square (pixels)**, **Grid starts at
x** and **Grid starts at y**, or press **Find the grid** again. For a picture with no grid lines, type
how many **Squares across** it is.

Then press **Read the map**. It makes a new map from the picture (undo puts the old one back):

- **The ground**: every square sorted by its colour into grass, dirt, cobbled road, paving, sand,
  water, rock or fields.
- **Buildings seen from above**: each roof is found as its own patch of colour with an outline
  drawn round it (a line, or a clean change of colour), at whatever angle it sits: a rectangle, or
  an L or a T of two or three, with the grid's own lines taken out first so they don't pass for an
  outline and the two slopes of a pitched roof (one lit, one in shade, the ridge between them)
  joined into one. What's cobbled or gravelled inside (strokes running every way, where a roof's
  tiles run in rows) is a road or a yard, not a roof; so is a strip that runs off the picture at both
  ends. A roof square to the grid becomes a building on whole squares, matched to its footprint; one
  at an angle keeps its angle (see **Tilt** on the **Picked** tab). Each gets walls round it, a door
  towards the road, windows and a roof whose covering is guessed from its colour (red: clay tiles,
  yellow: thatch, brown: wooden shingles, grey: slate). Its size guesses how many floors it has and
  what it is (a shed, a house, a hall, a tower).
- **Buildings drawn as floor plans**: rooms closed in by walls drawn in dark lines. Rooms that share
  a line are one building, with that line as a wall inside it, and a gap in its outside line is its
  door.
- **Trees**: the green squares that are clearly darker or busier than the usual grass, and the
  yellow-green ones busy with leaves; a clump of a few squares is one tree, sized to it.

The settings:

- **Buildings are**: **Worked out from the picture** looks for both; pick **Seen from above
  (roofs)** or **Drawn as floor plans (walls in lines)** when you know which.
- **Find buildings**, **Find trees**: untick either to leave them out.
- **Roofs**: how much of a roof's outline has to show. Further left finds more roofs (and more
  mistakes: sheds, carts, patches of yard), further right only the clearest.
- **Biggest (squares)**: anything bigger than this is ground (a market square, a field), not a building.
- **Use my saved buildings where one fits**: a rectangular roof the same size as one of your saved
  buildings (either way round) gets that building, turned so its door faces the road, instead of one
  made up.
- **Show the picture under the plan** and **Ground colours** (how strongly the ground read is painted
  over the picture), to check one against the other.

It's a best guess: paint the ground, move, turn, copy and change the buildings to put right what it
got wrong.

## What's picked

![A house picked on the plan and its settings on the Picked tab: name, type, district, tags, its roof and covering](images/window-citybuilder-picked.png)

Click a building with **Select** and the **Picked** tab shows it:

- **Name**, **Type** (house, shop, tavern, temple…: anything you like), **District** (residential,
  market, docks…) and **Tags** (words to find it by, with commas between). The name shows on the plan.
- **Tilt (°)**: a building read off a map at an angle keeps its angle, clockwise degrees past its
  quarter turns, turning about its own middle; type another, or 0 to straighten it. **Square it to
  the grid** straightens it and puts it back on whole squares. (A tilted building draws, picks, turns
  and cuts like any other; it stands on every square it covers any part of.)
- **Turn ⟳** turns it a quarter turn. **Add a floor** puts another floor on top (the top floor's
  walls and windows again; doors upstairs become windows) and moves its roofs up onto it; **Take the
  top floor off** does the opposite.
- **Roof** (gable, hipped, lean-to or flat) and **Covering** change all its roofs at once; **Roof it
  again** puts new roofs over the top floor's rooms, a rectangle at a time.
- **Edit in the building editor…** opens it in the [building editor](houseeditor.md): change
  anything (rooms, walls at any angle, doors and windows, furniture, stairs, roofs), close the editor,
  and the building on the map is changed to match.
- **Copy it** puts a copy beside it; **Take it away** takes it off the map.
- **Save as a building to reuse** saves it with the building editor's saved buildings (with its name,
  type, district, tags and a picture), so you can put it down again and again with **Place**, and it's
  in the building editor's saved buildings too.

With several things picked, the **Picked** tab says what they are. Type a **Type** or **District**
there and every building picked gets it; with only trees and things picked, change what they are and
their **Size** together. **Turn ⟳**, **Copy beside it** and **Take away** do what `R`, `Ctrl+D` and
`Delete` do. A label picked on its own shows its **Text** and **Size** to change.

## The town's buildings

![The Town tab listing the sample village's buildings with their type and district, and the plan coloured by district with a key](images/window-citybuilder-town.png)

The **Town** tab lists every building on the map with its type, district and size. Type in the box to
find one by its name, type, district or a tag, or show just one **Type** or **District**; click one to
pick it and go to it on the plan. Under the list it counts the buildings of each type.
**Colour the plan by district** shows each building in its district's colour (the same colour every
time for the same name), with a key underneath.

## Saved buildings

On the **Tools** tab, **Saved buildings** lists the buildings saved to use again (here, or in the
building editor), with their type and size. **Type** shows just one type. Click one and the **Place**
tool is picked: click the map to put it down. **Look again** reads the list again after saving one in
the building editor.

## The 3D view

![The sample battle map's town in 3D: plain blocks for the houses with doors and windows cut in, roofs coloured by their covering, the river, the roads and the trees](images/window-citybuilder-battlemap-3d.png)

**3D** shows the whole map put together: drag to turn it round, scroll to zoom in on a part,
middle-drag or the arrow keys to move it, and press `F` (or double-click) to see it all again. Untick
**Roofs** to look into the buildings, **Trees and things** to see the ground clearly, or **Doors and
windows** to make a very big town quicker still. The building picked shows orange.

![The sample village in 3D](images/window-citybuilder-3d.png)

## Cutting the buildings

**Put this building in the design** puts the picked building's pieces in the design, the same way
the building editor's **Put in the design** does: nested onto the bed, a page per bed, its walls,
columns, floors, tiles and roofs. **Put every building in the design** does it for every building on
the map, one after the other, each on pages of its own; a big town is a great many pieces, so it may
be better to do a few buildings at a time.

Each building is made at its own grid square: open it in the building editor to change its sheet,
walls (box or flat), textures and sizes before cutting.

## Cutting the ground, bridges, walls, trees and scenery

![The Cut tab for a walled town: the ground in tiles four squares across with jigsaw edges, the bridges where roads cross the river, the town wall's sections, corners, gatehouses and guard towers, the trees, and the market square's scenery](images/window-citybuilder-cut.png)

The **Cut** tab puts the rest of the town in the design. Tick **Only the last box dragged with
Select** to cut just the part of the map in the box you last dragged with **Select**; otherwise it's
the whole map.

**Put the ground in the design** cuts the map's ground as floor tiles that lock together, the
[floor tile maker](floortiles.md)'s way, each square etched with its own ground: cobbles on the roads,
flagstones on paving, sand, rock, tufts of grass, pebbles on dirt tracks, ripples on water and furrows
in the fields. Squares under the buildings are left plain (each building stands on its own floor),
with the building's outline scored round them.

![The ground by the river cut as tiles: the cobbled road crossing the river, ripples on the water, tufts of grass, plain squares where buildings stand, the grid scored and jigsaw edges between the tiles](images/citybuilder-ground.png)

- **Tile size (squares)**: the biggest tile, in squares each way; each must fit on the bed.
- **Sheet (mm)**: how thick the sheet is.
- **Edges**: how the tiles join: **Jigsaw edges**, **Clips**, **Magnets** or **Plain edges**. The
  edges round the outside of the map (or the box) are plain.
- **Score the grid** and **Score where the buildings stand**: the grid lines, and each building's
  outline, scored on.

Under them it says how many tiles that is. A big town is a great many tiles, so at most 400 are made
at once: drag a box round a part with **Select** and cut it a part at a time.

**Put the bridges in the design** makes a bridge with the [bridge maker](bridges.md) wherever a road
crosses water: stone arches for a cobbled or paved road (flagstones on a paved one), planks for a dirt
track, as wide as the road and reaching half a square onto each bank, in the map's look. Each goes on
pages of its own.

**Put the town walls in the design** makes the town wall and its guard towers with the
[furniture maker](furniture.md)'s town pieces, in the map's look (a stone wall with a wall walk and
merlons, a fortress wall, a log palisade, an armoured defence wall, a sandstone wall or a gothic city
wall): a corner piece wherever the wall turns (the parapet round the outside of the turn, or the
inside where it turns in), a gatehouse over each road through a gap two squares wide, an end with a
turret wherever else it stops (at a river, say), and the rest in sections two squares and one square
long. The wall isn't cut under a guard tower: the sections either side meet it. Under the tab it says
how many of each that is.

**Put the trees in the design** makes the map's trees as the furniture maker's: two profiles halved
together in a round base, bark and leaves etched, in the map's look (oaks and pines, dead trees,
palms, giant mushrooms, or trees in planters), each as big as it is on the map (to the nearest quarter
square, so trees of a size are cut together). At most 150 at once: a big town's woods are cut a box
at a time. Bushes aren't cut.

**Put the scenery in the design** makes the [scenery maker](scatter.md)'s pieces for the map's wells,
carts, boulders, crates (as barrels) and fences (each run of fence in sections of up to four
squares), and the furniture maker's horse troughs, players' stages, market stalls, outdoor bars,
notice boards, fountains, gallows (there's no sci-fi gallows, so a sci-fi map gets the medieval one), street
lamps and statues, each in the map's look: a lantern post, a brazier, a torch or a light pylon, and a knight, an angel,
a warlord, an idol, a spearman or a trooper on a pedestal. Hedges and stone walls aren't cut.

Each set goes in the design with its settings, like anything the furniture maker makes: right-click
one of its pieces and choose **Edit furniture…** to change its sheet or sizes.
