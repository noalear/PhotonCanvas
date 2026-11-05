# Building editor

An add-on (Settings → Add-ons → **Building editor**): on when PhotonCanvas is installed, and can be turned off. It needs the **Textured wall maker**, **Mini furniture maker**, **Floor tile maker**, **Stairs, ladders and floors**, **Roofs and towers** and **Whole-building kits** add-ons, which make its pieces. Its button is **Home → Draw → Build**, and **Edit in the building editor…** is on the right-click menu of the pieces it made.

**Build** lets you draw a building yourself, floor by floor, the way you build a house in a
life-sim game: drag out rooms, draw walls along the grid or corner to corner, drop doors and windows
anywhere along a wall and slide them about, give every floor its own shape, and watch it go up in
3D with the walls in front cut away so you can see into the rooms. Put roofs over it (gable, hipped,
lean-to or flat, at any pitch), railings go round balconies and terraces by themselves, and each room
can have a floor pattern of its own. Stand furniture in the rooms and turn it, put stairs and ladders
up to the floor above, give any wall a texture of its own and any column a shape of its own, and see
the engraving sunk into the pieces before you cut. Every wall, column, floor, roof and floor tile then comes out as
pieces to cut, made the way the wall maker ([Textured walls](walls.md))
and the building maker ([Whole buildings](buildings.md)) make them: textured box walls whose tabs
go into slotted columns, floors that lie on the walls, and jigsaw floor tiles to stand it on.

> **Note:** The building editor has been checked on screen and in tests (every wall, column and
> floor is put together in 3D and checked for clashes), but not yet cut on the machine. Cut one
> column and the end of one wall first, check the wall's tabs go into the column's slots, and change
> **Slot fit** on the **Look** tab if they're too tight or too loose.

## The Building editor window
<!-- windows: HouseEditorWindow -->

![A two-storey house in the building editor: the ground floor's plan on the left, the house in 3D with the near walls cut away in the middle](images/window-houseeditor-ground.png)

Click **Build** on the **Home** tab (in the **Draw** group). The **Building editor** opens on the
last building you drew (a small two-room house the first time). Along the top are the tools and the
floors; on the left is the **Plan**, the floor you're on seen from above on a grid of squares; in the
middle, **Put together** shows the whole building in 3D and **Pieces** shows every piece flat; down
the right, **Picked** shows the door, window, room or roof picked on the plan and **Look** the walls'
look and sizes. Under them are the number of pieces and notes: how it goes together, a red ✕ that has to be
fixed before it can go in, a yellow ! worth a look.

Press **Put in the design** and every piece goes into the design, nested onto the bed, a page per
bed. To change it later, select any of its pieces, right-click and choose **Edit in the building
editor…**, change it and press **Update in the design**: the old pieces are replaced wherever they are.

## Drawing on the plan

Pick a tool along the top (or press its letter), then use it on the plan:

| Tool | Key | What it does |
|---|---|---|
| **Select** | S | Click anything on the plan to change it on the **Picked** tab: a door or window (drag it along its wall; **Delete** takes it away), a wall, a column, a railing, a roof, a piece of furniture or a way up, or inside a room to give it a floor of its own. Where a roof covers a room, click again to swap between the roof and the room. |
| **Room** | | Drag from one grid point to another: four walls round the rectangle at once. Drag a room against another and they share the wall between them. |
| **Wall** | W | Drag from one grid point to another: a wall straight along, straight across or corner to corner (diagonal). It snaps to the nearest of those as you drag, and shows how long it is. |
| **Erase** | E | Drag along walls to take them away, click one square of wall, or click a door or window. |
| **Door** | D | Click a wall to put a door in it. Keep the button down and drag to slide it along the wall. Tick **Opens on hinges** to make it swing. |
| **Doorway** | O | An open doorway with nothing in it, between two rooms, say. |
| **Window** | N | Click a wall to put a window in it, and drag it along. |
| **Floor** | L | Drag over squares to lay floor outside the rooms: a balcony, a landing, the floor of a walkway. Start a drag on a laid square to take floor up. |
| **Furniture** | U | Pick a piece on the right (a table, bed, chest, bookshelf…), then click in a room to stand it there. Drag it about; **R** turns it 45° (**Shift+R** back), and the mouse wheel as you drag, or **[** and **]**, turn it a quarter. **Delete** takes it away. |
| **Stairs** | A | Click to put a way up to the floor above: open stairs, solid stairs, spiral stairs or a ladder. An opening is cut in the floor above over it. Drag and turn it like furniture. |
| **Roof** | T | Drag a rectangle over rooms for a roof on top of this floor's walls; click a roof to change it on the **Picked** tab. **Delete** takes the picked roof away. |

Walls that close a space in make a **room**: it's shaded on the plan and gets a floor above it. Scroll
on the plan to zoom (toward the pointer), drag with the right or middle button to move about, and
press **F** to fit the whole building in. **Undo** and **Redo** (Ctrl+Z and Ctrl+Y) go back through
every change.

Each straight stretch of wall between two columns is one wall, labelled with a letter on the plan (on
its inside); its pieces are named after it ("Wall C: base", "Floor 2 walls A, D"), so you know where
each goes. A column goes wherever walls meet or end: a corner, a T, a cross, or an angled column where
a diagonal wall comes in (a stack of layers on a pin). Walls that come out the same are cut together.

A new door or window is like the one picked before it (the same shape and size), so pick one you
like and click along the walls to put more of the same in.

### A wall or a column of its own

With **Select**, click a wall: the **Picked** tab shows that stretch, column to column, and its
**Texture**, **Wear %** and **Detail size** can be its own (a brick wall in a stone house, a more
battered wall at the back). A wall with a look of its own is drawn lighter on the plan, and where a
straight wall's look changes it's cut there with a column between. **Make every wall on this floor
like it** copies its look along the floor; **Like the rest of the building** puts it back.

**Other side** gives the wall a different texture on its other side (stone outside, wood panelling
inside), with its own **Other side's wear %**. The line above it says which way the wall's texture
faces: out of the building for an outside wall, so the other side is the inside. A wall between two
rooms says which way on the plan each side faces.

- **Single-panel walls**: the other side is engraved on the panel's back after it's cut. **Put in
  the design** adds a page after the walls' sheet, "… · backs (turn the pieces over)", with each
  piece's back on **Building · back etch** and its outline on **Building · back outline (guide)**,
  which isn't burned. The outline is named with the piece's number. Cut the sheet, turn those pieces
  over left to right, select each piece's group on the backs page and use **Camera** → **Fit to
  piece**: only the outline is matched, and the engraving moves with it. Then run the page. See
  [The back of a single panel](walls.md#the-back-of-a-single-panel).
- **Box walls**: the back face is a piece of its own, so it simply gets the other side's texture.

Click a column to give it a **Shape** of its own: square, octagonal, round, hexagonal, chamfered or
fluted. A shape a wall can't meet at that corner (a square column where walls meet at 45°, say) is
made to suit instead, with a note. **Built as** makes it *a box* or *stacked layers on a pin*, and
**Texture** picks what's etched on a boxed column's sides. **Make every column on this floor like it**
does the rest. **Column shape**, **Columns built as** and **Column texture** on the **Look** tab are
the building's own.

A boxed column of any shape has a side standing on each of its flats (a round one is made with 12, 16
or 24 flats, to suit its walls). Each side has a tab at the bottom that stands in a slot in the base
plate, and one at the top that goes up into a slot in the top plate, so the box locks together; caps
go on top. Thick walls' tabs go into slots in the sides they meet; a single-panel wall's end goes
between the two halves of its side, through notches in the plates. Octagonal and hexagonal sides are
narrower than a square's, so those boxes need wider columns: a note says how wide. Fluted columns are
always stacked. As usual, square columns of thick walls are boxes and the rest are stacked.

### What a wall can hold

- One door or doorway to a wall, and windows all of one kind. Put two doors on one stretch, or two
  different windows, and the wall is cut in two at a grid point between them with a straight column
  there (a note says if they're too close together to be split: move one a square along).
- A wall longer than the bed is cut the same way, with a straight column, where it misses the doors
  and windows.
- A door or window that ends up off a wall (its wall was erased) or at a corner is left out, with a note.
- A wall is at least a column's width long between its columns: a diagonal wall one square long is
  too short for box walls, so draw diagonals two squares or more.

## Floors

**Floor** at the top picks which floor you're drawing (1 is the ground). **Add a floor above** adds an
empty one on top; the floor below shows faintly under it, so you can draw over it. **Copy walls up**
draws the floor below's walls on this floor too (not its doors and windows), to start a storey the
same shape. **Remove this floor** takes it away.

Every floor can be a different shape. The floor between two storeys covers every room under it and
on it, and any floor you lay by hand, so an upstairs smaller than the ground floor stands on a floor as
big as the ground floor, and the rest of it is a terrace you can step out onto through a door.

**Yard** on the **Look** tab (under **This floor**) limits that: how many squares this floor's floor
reaches past its own rooms and walls. *None* stops it at the walls; *1 square* leaves a narrow
balcony round them; *As drawn* is as above. Upstairs, the rooms below that the floor no longer covers
are left open on top, for a roof of their own (a note says so until they have one). On the ground
floor, the yard is how far the base (tiles or plate) reaches all round the building. A floor with a
yard set shows where its floor reaches on the plan, with a dashed edge. The columns under it are flush with its underside; the walls
above stand on it, and lines are scored on it where each wall and column goes. A floor bigger than the
bed is cut in pieces along walls under it, so each join rests on a wall top (a note says if a join has
no wall under it: glue a strip of sheet under it).

Walls drawn upstairs with nothing under them get a red ✕: close in a room under them, or lay floor.

### Railings

Every edge of a floor with no wall standing on it gets a railing, about a metre high for the figures
(a top rail and a bottom rail with balusters between): round a terrace on the roof of the rooms below,
round a balcony of floor you laid. They show on the plan as a rail with dots. Each straight stretch is
one piece with two tabs under it that go into slots cut in the floor; it stops against a column, and
at a corner the one running left to right carries on past the corner and the other butts up against
it (glue them there). Turn **Railings round its open edges** off on the **Look** tab (under **This
floor**) for a floor with none.

### A room's own floor

Pick **Select** and click inside a room: the **Picked** tab shows the room, and **Floor** gives it a
pattern of its own, any of the floor tile maker's (floorboards, flagstones, cobbles, herringbone brick,
chequered tiles, a sci-fi deck…). Its name shows on the plan. Upstairs it's etched on that room's part
of the floor; on the ground floor it's etched on the base plate, or, on floor tiles, it's a loose floor
that drops in between the room's walls and columns ("Floor 1, room 2 (chequered tiles)"). *The
building's floor* puts it back to the rest.

![The upstairs of the same house: a smaller room with a door out onto a terrace with railings round it, and a roof over it](images/window-houseeditor-upstairs.png)

## Roofs

Pick **Roof** and drag a rectangle over the rooms on the plan: a roof goes on top of that floor's
walls, resting on the column caps round it, and lifts off to show the rooms (the 3D view puts it on
with **Show** at *Everything, roofs on*). A roof over exactly one room has a frame under it that drops
inside the walls so it can't slide; over more than one room (or part of one) it has none, and a note
says so: glue it on, or let it rest on the caps. **Roof it** at the top puts one roof over the top
floor's rooms (one over them all if they make a rectangle together, otherwise one over each
rectangular room).

Click a roof with **Roof** or **Select** to change it on the **Picked** tab:

- **Shape**: *Gable* (two slopes with an end wall at each end), *Hipped* (sloping on all four sides,
  the ridge along the longer side), *Lean-to* (one slope, with a back wall and sides) or *Flat* (one
  plate, with a floor pattern etched on it).
- **Runs**: which way a gable's ridge runs, or which way a lean-to slopes down.
- **Pitch** (degrees from flat), **Covering** (slate, wooden shingles, fish scale, clay tiles, thatch,
  planks, corrugated metal; a flat roof takes a floor pattern), **Eaves** (how far it reaches past the
  walls) and a **Gable window** in each end wall.
- **Take the roof away**.

A new roof is like the one picked before it. Roofs are numbered on the plan ("Roof 2"), and their
pieces are named after them ("Roof 2: gable", "Roofs 1, 3: base" when two come out the same). A
roof's own pieces and how they go together are the roof maker's ([Roofs and towers](roofs.md)).
A roof under the floor above, or two roofs that overlap, get a yellow !. The floor below's roofs show
as a dashed outline on the plan above them.

Where two slopes meet (a ridge, or a hip running down a corner), each slope has a cap strip glued
along the seam: one lies along the joint and the other laps over it, so they close up tight with no
gap at the top and no step where the caps cross. Glue the caps marked *under* first.

## The 3D view

**Show** picks how much of the walls the 3D view draws:

- **Everything, roofs on**: all the walls, and the roofs.
- **Walls cut away**: the walls on your side of the building that face you (or face away from you) are
  left out, wherever you turn it, so you can see into the rooms, like a doll's house. The roofs are off.
- **Walls down**: no walls, just the columns, the floors and the tiles: the plan in 3D.

**up to** shows the floors only up to one of them, to look into the rooms of a lower floor.
**Engraving** shows the textures sunk into the pieces as deep as you'll etch them (0.3 to 1.5 mm), so
you can see how they'll stand out; *Painted on* draws them flat, which is quickest on a big building.
**Doors open** swings every door that opens out on its hinges (and slides portcullises up), to check
which side each is hinged. Drag to
turn the building, scroll to zoom, middle-drag or the arrow keys to move it, and the buttons in the
corner to fit it in again or go back to the first view.

## Doors and windows

On the **Picked** tab, with a door or window picked on the plan:

- **Kind**: *Door that opens*, *Doorway (left open)* or *Window*.
- **Shape**: square, a round or pointed arch, round, an arrow slit, angled corners (sci-fi) or rough (a cave).
- **Width** and **Height** (mm); a window's **Sill** is how high its bottom is above its floor.
- A door's **Looks like** (planks, studded, a metal blast door, a portcullis that slides up, a stone
  slab). As it comes, a door is glued into its wall and fills the whole opening, arch and all. Tick
  **Opens on hinges** for one that swings outwards, and pick which side it's **Hinged on the** (seen
  from outside); the plan draws its swing. In box walls it turns on pins inside the wall: only a
  square-topped leaf can swing inside a thick wall, so an arched or pointed door that opens has a
  fixed panel over it. In single-panel walls it's the furniture maker's door: it hangs on two clips
  slid onto the side of the doorway and covers the doorway a little all round (square, arched and
  pointed doorways). It needs about 7 mm of wall between the doorway and the column on its hinge
  side, for the clips; a note says if there isn't room.
- A window's **Bars** and, in box walls, what's **In the window**: a frame with its bars, a shutter
  glued in, shutters that open, or nothing.

**Make every one on this floor like it** makes every other door (or window) on the floor the same.
**Take it away** removes it.

## The look

On the **Look** tab:

- **Theme** and **Figures**: the same themes as the other terrain makers (medieval, dungeon, sci-fi,
  gothic, desert…) and the figures it's for, which scales the walls, doors and windows.
- **Walls**: *Thick* (boxes that stand on their own; **Wall thickness**) or *Single panel* (one
  sheet thick, in slimmer columns of stacked layers; picking it makes the columns slimmer too). **Panel thickness** is the sheet everything is cut from,
  **Columns** how wide each column is, **Storey height** how high each floor's walls are (rounded to
  whole layers of the sheet).
- **Texture** for the ground floor's walls and **Upstairs**, etched as grooves or scored lines.
- **Slot fit** and **Layout** (a different random layout of stones and planks).
- **Floors**: the **Base** the building stands on (the floor tile maker's *jigsaw floor tiles* over
  the walls' outside, *one plate* shaped to the building with lines scored where the walls and columns
  go, cut along walls only if it's bigger than the bed, or *nothing*), the **Ground floor** pattern on
  it, and the **Upper floors** pattern on the floors between storeys: any of the floor tile maker's
  (floorboards, flagstones, cobbles, tiles, sci-fi deck…). A room's own pattern goes over these.
- **Square**: the plan's grid square (25.4 mm, an inch, for most tabletop games).
- **Cut with** and **Etch with**: your material's settings, so slots come out their drawn width.
- **Number the pieces**: each piece's number scored on it (see below).

![The same house's pieces, laid out flat](images/window-houseeditor-pieces.png)

## Furniture

Pick **Furniture** (U): on the right, pick a **Theme** and a **Room** and the list shows the furniture
maker's ready-made pieces for them (a four-poster and a wardrobe for a medieval bedroom, bunks and
lockers for a ship's quarters, a sarcophagus for a gothic crypt…), with a line about the one you
click. *Plain pieces* lists one of every kind the furniture maker makes. The theme starts as the
building's own, but any theme's pieces can go in any building and keep their look. Click one, then click in a room to stand
it there; the next click puts down another the same way round. The plan draws each piece as its own
pieces seen from above (a bed's head board and pillow, a chair's back, a bookshelf's sides and shelves),
darker low down and lighter higher up, so you can see which way it faces. Its dark edge, with a notch
pointing out, is its front, and its name shows over it when the pointer is on it.
Drag it about (it snaps to an eighth of a square; hold **Shift** to place it to the millimetre). **R**
turns it 45° clockwise, **Shift+R** back, so it can stand across a corner; the mouse wheel while dragging,
or **[** and **]**, turn it a quarter. With nothing picked, **R** turns the next piece you put down.

Click a piece (with **Furniture** or **Select**) to change it on the **Picked** tab: turn it, change
anything the furniture maker can (its style or kind of front, its sizes, legs, back, shelves, lid,
bedding, top, theme and the rest, as suits the piece), **Put down more like it**, or **Take it away**. Each piece is made
by the furniture maker ([Mini furniture](furniture.md)) from the furniture's own sheet, so it slots and
glues together the same way; pieces that come out the same are cut together ("Table: top", ×3). A piece
standing in a wall or column, off the edge of its floor, in the way of the stairs or over their opening,
or running into another piece gets a yellow !. Rugs lie flat under the rest, and hangings (banners,
tapestries, shields, screens) are glued up the wall at their own height: put them against a wall.

## Stairs and ladders

Pick **Stairs** (A), choose what to **Put in** (open stairs with a handrail, solid stairs of stacked
layers, spiral stairs or a ladder) and click on the plan: it climbs from this floor to the one above,
and an opening is cut in that floor over it (shown dashed, with an arrow the way it climbs; the floor
above shows the opening dark). Drag and turn it like furniture (stairs only turn a quarter at a time); on the **Picked** tab change its
**Kind** (a hooked ladder hangs from the opening's edge), turn it, or take it away, and change the stairs
maker's settings for it: step height and depth, width and handrail for stairs, radius, turn per step,
centre, pin and which way it turns for spiral stairs, width, rung spacing and lean for ladders, and
whether the floor's texture is etched on the steps. Left alone, these suit the building's figures;
**Size them to suit the figures** puts them back. Ways up made the same are cut together; different ones
are numbered ("Stairs 1", "Stairs 2"). Its pieces are the stairs maker's ([Stairs and floors](stairs.md)).
Stairs on the top floor, stairs running into a wall, an opening coming up into a wall above or past the
edge of the floor above, and two ways up that cross get a yellow !.

## Piece numbers and the building booklet

Every piece gets a number, in the order the building goes together: the floor tiles and base first,
then each floor's floor, railings, columns, walls, stairs and furniture, and the roofs last. Pieces
that come out just alike share a number. With **Number the pieces** ticked (on the **Look** tab) the
number is scored small and light on each piece, as single lines, low down where it's clear of the
texture; a piece etched all over gets a little plain patch for it. **Pieces** shows the numbers in
blue and in each piece's name ("#12 Wall A"). In the design they're on a layer of their own,
*Building · numbers*, so you can turn it off to cut without them. Numbers that would read as
another number upside down (6 and 9, 16 and 91) are underlined.

**Building booklet** (beside **Put in the design**) makes step-by-step building instructions as a
PDF, like a building set's, and opens it:

- a cover with the finished building, how many pieces and what else you need;
- every piece, with its number, how many to cut and its size;
- then a step a page, from the ground up: the pieces it takes (with their numbers and how many), and
  the building so far with the new pieces picked out in orange and their numbers on them. Walls go up
  a few at a time, and the steps that go inside the rooms (furniture, stairs) leave out the walls in
  front so you can see where things go.

It's saved beside your design (or, before the design is saved, in the data folder's *Booklets*), named
after it.

