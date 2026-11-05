# City builder

An add-on (Settings → Add-ons → **City builder**): on when PhotonCanvas is installed, and can be
turned off. It needs the **Building editor** and **Map to terrain** add-ons (and the makers they use):
every building on the map is a building editor building, and the battle map reader finds a picture's
grid the way Map to terrain does. Its button is **Home → Draw → City**.

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
the left are four tabs: **Map** (its name, look and size, and your saved maps), **Battle map**
(reading a picture), **Tools** (what the tools put down) and **Building** (the building picked). On
the right, **Plan** is the map from above and **3D** shows it put together.

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
| **Select** | Click a building to pick it (its settings show on the **Building** tab) and drag it to move it, a whole square at a time. While dragging, the mouse wheel turns it; `R` turns the picked building a quarter turn (Shift+`R` the other way), and `Delete` takes it away. |
| **Building** | Drag out a rectangle: a new building stands on it, with walls round it, a door towards the nearest road or paving, windows along the walls and a roof (as many floors as **Floors** on the **Tools** tab says). With the right button, click a building to take it away. |
| **Place** | Put down a saved building (pick it on the **Tools** tab first): click where its top left corner goes. `R`, the right button or Ctrl + the mouse wheel turns it before you click. |
| **Paint** | Paint the ground with the **Ground** picked on the **Tools** tab, as many squares across as **Brush (squares)**. The right button paints grass. |
| **Fill** | Click a patch of one ground to fill it all with another (the right button fills it with grass). |
| **Fence** | Drag along the grid lines to put up the **Fence** picked on the **Tools** tab: a fence, a hedge, a stone wall or a town wall. Start on one that's there to take it away instead. |
| **Trees** | Click to put down the **Tree or thing** picked on the **Tools** tab: a tree, a pine tree, a bush, a boulder, a well, a cart, crates and barrels, a lamp or a statue. The right button takes them away. |
| **Erase** | Drag over trees, fences and buildings to take them away. |

**Undo** and **Redo** (`Ctrl+Z`, `Ctrl+Y`) go back and forward a step at a time.

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

## Reading a battle map
<!-- windows: CityWindow -->

![The sample battle map read into a town: the river, the cobbled roads and paved square, every house with its roof, and the wood as trees, over the picture](images/window-citybuilder-battlemap.png)

On the **Battle map** tab, press **Open a picture…** and pick a top-down map of a town (PNG, JPEG,
WebP…), or **Use the selected picture** for one already in the design, or **Try a sample battle
map** to see how it works. The picture shows under the plan and its grid is found by itself: check
**Square (pixels)**, **Grid starts at x** and **Grid starts at y**, or press **Find the grid** again.
For a picture with no grid lines, type how many **Squares across** it is.

Then press **Read the map**. It makes a new map from the picture (undo puts the old one back):

- **The ground**: every square sorted by its colour into grass, dirt, cobbled road, paving, sand,
  water, rock or fields.
- **Buildings seen from above**: patches of one colour that stand out from the ground round them and
  are building-shaped (compact, two squares or more each way, not part of a long road, not green or
  blue) are roofs. Each gets walls round it, a door towards the road, windows and a roof whose
  covering is guessed from its colour (red: clay tiles, yellow: thatch, brown: wooden shingles,
  grey: slate). Its size guesses how many floors it has and what it is (a shed, a house, a hall, a
  tower).
- **Buildings drawn as floor plans**: rooms closed in by walls drawn in dark lines. Rooms that share
  a line are one building, with that line as a wall inside it, and a gap in its outside line is its
  door.
- **Trees**: the green squares that are clearly darker or busier than the usual grass.

The settings:

- **Buildings are**: **Worked out from the picture** looks for both; pick **Seen from above
  (roofs)** or **Drawn as floor plans (walls in lines)** when you know which.
- **Find buildings**, **Find trees**: untick either to leave them out.
- **Roofs**: further left finds more roofs (and more mistakes), further right only the clearest.
- **Biggest (squares)**: anything bigger than this is ground (a market square, a field), not a building.
- **Use my saved buildings where one fits**: a rectangular roof the same size as one of your saved
  buildings (either way round) gets that building, turned so its door faces the road, instead of one
  made up.
- **Show the picture under the plan** and **Ground colours** (how strongly the ground read is painted
  over the picture), to check one against the other.

It's a best guess: paint the ground, move, turn, copy and change the buildings to put right what it
got wrong.

## The building picked

![A house picked on the plan and its settings on the Building tab: name, type, district, tags, its roof and covering](images/window-citybuilder-picked.png)

Click a building with **Select** and the **Building** tab shows it:

- **Name**, **Type** (house, shop, tavern, temple…: anything you like), **District** (residential,
  market, docks…) and **Tags** (words to find it by, with commas between). The name shows on the plan.
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
  type, district and tags), so you can put it down again and again with **Place**.

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
walls (box or flat), textures and sizes before cutting. The ground, fences and trees are only shown in
3D; they aren't cut.
