# Finish stacks on the rotary

**Finish it on the rotary** is an add-on for [stacked slices](3d-slices.md). It puts a dowel hole up
the middle of every slice and, when you make the slices, adds a page that engraves the glued stack
down to the model's real shape on the rotary: the stack turns on its dowel in the chuck, and the
laser takes the steps off the outside, a pass at a time.

It works only with **Build as: Stacked layers (solid)**, and needs a rotary with a chuck (Laser →
**Work mode**; a homemade one is **Your own rotary**, see [Work area](work-area.md#your-own-rotary)).

> **Warning:** Not yet tried on the machine. The commands follow xTool Studio's own rotary
> commands, but finishing a stack hasn't been run on a real one. Try a small model in scrap first,
> and stay by the machine.

## Finish a stacked model on the rotary

1. Open a model in **3D to slices** (Home tab) and choose **Stacked layers (solid)**.
2. Choose **Outer: sand the steps back to the shape** for the outline. The slices then hold all of
   the model, with the steps sticking out, so there's material to engrave down. (With the other
   outlines the steps cut into the model, and the laser can't put that back; the summary says so.)
3. Tick **Finish it on the rotary**, and set the dowel and the engrave settings below. The summary
   says where the dowel goes, the **Object Ø** to set on the rotary, how much comes off and at how
   many focus heights.
4. Click **Make the slices**. The slices go on their pages as usual, and a page "… on the rotary"
   is added after them.
5. Cut the slices, push the dowel through them and glue the stack up on it, slice 1 at the bottom,
   lined up by the pins and the marks. Glue the dowel in.
6. Fit the stack in the rotary's chuck by the dowel. Choose Laser → **Work mode** → **Rotary**
   with your chuck, and type the **Object Ø (mm)** from the summary. Open the "… on the rotary"
   page.
7. Turn the stack so the notch in the dowel hole points straight up, and focus on the slice the
   summary names, on that top line. Frame (it traces a line along the stack) and ship it.

| Setting | Range (default) | What it does |
|---|---|---|
| **Finish it on the rotary** | off | Turns it on. |
| **Centre dowel Ø** | 2 to 30 mm (6) | The dowel the chuck holds. A hole this size goes through every slice, with a small notch on one side that points to the start. Set the cut layer's kerf for a snug fit. |
| **Engrave with** | your library's engrave settings with a measured depth (Default engrave settings) | The settings every focus height engraves with. Measure the depth a pass first with the etch rate test (Laser tab), saved to the library. |
| **Depth a pass (mm)** | 0.01 to 2 mm (0.1, or the chosen settings' depth) | How deep one pass goes on a flat piece with those settings. |
| **Focus band (mm)** | 0.2 to 5 mm (1) | How far the surface may be from the focus. Smaller is sharper, with more focus heights. |
| **Deepest (mm)** | 0.2 to 20 mm (3) | The most taken off anywhere. Spots that need more are left for sanding (the summary says how much). |
| **Wall kept (mm)** | 0.5 to 10 mm (2) | At least this much is left over the dowel hole, pin holes and a hollow, so the engraving never breaks through. |
| **Detail (mm)** | 0.1 to 2 mm (0.25) | How finely the surface is mapped. Smaller follows the shape closer, but the page takes longer to make. |
| **Slice 1 on the right** | off | The stack goes on the rotary with its bottom on the right. Off: slice 1 on the left, as the page shows it. |

## Where the dowel goes

The dowel goes where a straight line runs through every slice with room for it (the dowel, the
wall and the notch), at the spot the stack swings least round: the nearer the surface stays to the
dowel all the way round, the less the focus has to move. If there's no straight way through every
slice (a model standing on two legs), the summary says so and nothing is cut: stand the model
another way up, or use a thinner dowel.

The pins between slices keep clear of the dowel, and **Hollow it out** (the hollow models add-on) keeps solid
material round it, so you can use both.

## How the engraving is worked out

The stack isn't round, so the page goes by angle: once round the dowel is the page's height, and
along the stack is its width (slice 1 at the left). At every spot it compares how far out the
stack is with how far out the model is there, and takes the difference off, a pass at a time.

- **Focus heights.** Because the stack isn't round, the surface under the laser is nearer or
  further from the dowel as it turns, and gets nearer as passes take material off. So every pass is
  split by how far out the surface is at that moment, into bands **Focus band (mm)** deep. Each band
  is one layer, "Rotary · focus +1.5 mm" and so on, outermost first, with its **Focus offset (mm)**
  set so the focus is on that band. Each layer holds, for every angle, only the parts of each pass
  that are that far out. The head moves when the next layer starts and goes back at the end.
- **Passes.** A layer has a shape a pass ("Pass 3 at 18.5 mm out"), each filled once, in order.
- **Nearer the middle.** The page's lines are drawn the same distance apart a turn, so they're
  closer together on the surface nearer the dowel, and each pass goes a bit deeper there. That's
  allowed for: those spots get fewer passes.
- **The start.** The page has an orange **rotary start line** half way down: that line faces the
  laser at the start. On the stack it's the side the notch in the dowel hole points to, which is the
  model's front.
- **Object Ø.** The page is made for the stack's widest swing, rounded up to a whole millimetre.
  Set that **Object Ø (mm)** in Work mode, even though the stack isn't that size everywhere: with a
  chuck it sets how far a turn is, so the angles come out right.

The job checks warn when the work mode isn't the rotary, the **Object Ø** doesn't match the page,
the rotary is on rollers, or the head comes down more than 10 mm for the innermost focus height. It
does that while the widest parts of the stack still turn under it, so make sure there's that much
room under the head.

## Limits

- The laser fires straight down at the top of the stack, so it reaches the outermost surface at
  each angle. Hollows the beam can't see into (between legs, under a chin) are left; sand them.
- Steep faces (the top of a head, a shelf under an arm) take the beam at a slant and come out
  rougher than faces that face it.
- The stack must fit across the laser's field: 220 mm long at most.
- Depth a pass is measured flat; burnt wood takes the beam a little differently. Run the first
  focus height on its own and check the depth before the rest.

## Try it

- [Finish a stacked model on the rotary](tutorial:3d-rotary-finish)
- [3D models and stacked slices](3d-slices.md)
