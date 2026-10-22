# Troubleshooting

Problems and what to do about them, by what you see. The messages are the program's own words.

Where to look first:

- **Checks** in the job bar: what's wrong with the job, and anything worth knowing.
- The caption under the job bar's timeline: what the job is waiting for.
- The status pill at the right end of the ribbon: what the machine is doing.
- **Settings → Machine → MACHINE EVENTS**: everything the machine reported, with times.
- The `logs` folder in the [data folder](machine.md#the-data-folder): a diary per day, with the
  machine's own messages.

## Connecting

### PhotonCanvas needs its connection keys

*⚠ Before the first connection, PhotonCanvas needs its connection keys (a one-time setup).*, or
*The machine keys haven't been set up yet (Settings → Connection → Connection keys).*

PhotonCanvas doesn't come with the three codes xTool machines expect. Click
**Set up connection keys…** (or **Settings → Connection → Connection keys…**) and then
**Find them in my xTool Studio**. See [Connection keys](machine.md#connection-keys).

### The keys aren't found

- *xTool Studio wasn't found in the usual places.*: it's installed somewhere else. Use
  **Choose xTool Studio's program file…** and pick its `app.asar` (in its `resources` folder).
- *No keys found in that file.*: it isn't xTool Studio's `app.asar`, or a very new or very old
  xTool Studio keeps them differently. Try another version, or type them in.
- *Found 1 of 3 keys. The rest can be typed in below.*: type the others under
  **Or type / paste them in**.
- ✗ beside a key you typed: it isn't right. Check for spaces or a missing character.
- No xTool Studio at all: install it from xTool's website, set up the keys, then you can uninstall
  it.

### No machines answered

*No machines answered. Check the machine is on and on this network, and that your firewall allows
PhotonCanvas. You can also type the IP address.*

- Is the machine on, awake and on the same network as the computer?
- Your firewall must let PhotonCanvas through.
- Type the machine's address in the box and click **Connect**. Your router lists it.
- With nothing on the network, **Find machines** also tries a USB cable and a direct network
  cable. Over USB this hasn't been tried on the machine yet.

At start, *No machine found yet. Turn it on, then click Find machines or Connect.* or *Couldn't
connect automatically. Check the machine is on, then click Connect.* say the same.

### Connecting fails

- *Enter the machine's IP address (or use Find machines).*: the box is empty or isn't an address.
- *No answer from … within 10 s. Check that the machine is on and on this network.*: nothing
  answered at that address. The machine may have a new address: use **Find machines**.
- *Could not connect to … The machine may be off, on another network, or using the older V1
  protocol.*: the same, or a machine PhotonCanvas can't talk to.
- *…the machine refused pairing (409: another client may be connected)*: close xTool Studio or the
  phone app, then connect again.
- *…the session has expired; reconnect to the machine*: **Disconnect**, then **Connect**.

### The machine asks for its button

*Press the button on the machine to allow this computer.* Press the machine's button within 90
seconds. Too late: *…the button on the machine was not pressed within
90 s.* Click **Connect** and try again.

### The connection was lost

*Connection lost: …* (for example *No data from the machine at … for 11 s; the connection is
lost.*). The machine went quiet: it was turned off, went to sleep, lost Wi-Fi, or moved to another
network. Click **Connect** once it's back. A job loaded on the machine stays there, and the job
bar shows it again once connected.

### It's a different model

- *⚠ This is an xTool F1 Ultra, but xTool F2 Ultra is chosen: pick it in Settings → Machines so
  jobs fit its work area.*: choose the right one in [Machines](machine.md#machines-page).
- *⚠ PhotonCanvas doesn't know this model (…); jobs are made as for the F2 Ultra.*: be careful;
  try small jobs on scrap first.
- *⚠ This firmware does not list text G-code support; sending may fail.*: try a small job first.

Every model but the F2 Ultra is untested.

## Shipping a job

### Ship It is greyed out

The caption under the timeline says why:

- *Connect the machine (right end of the ribbon) to send or frame.*: click **Connect**.
- *Fix the problem in Checks to send.*: the job can't be built. Click **Checks** (it says
  **Can't build**) for the reason; the sections below cover them.
- It's also grey while a job is uploading or being got ready (**Shipping it…**).

### Nothing to process

- *There is nothing on the canvas to process.*: draw or import something.
- *No shapes are on an enabled, visible layer.*: every layer with shapes is hidden or has its
  output off (the lightning icon in the [Layers](layers.md) panel). Turn one on.
- *Nothing would be done.* followed by a layer: everything is on the wrong kind of layer (see
  [A layer is left out](#a-layer-is-left-out)).
- *There's nothing to mark in …*: the section being marked is empty (see
  [Work area](work-area.md)).

### Outside the work area

*Point (230, 40) mm is outside the work area (0–220 × 0–220 mm) (item 3).*

Part of the job is off the 220 × 220 mm bed. Move or shrink the design until it's all inside
(**Arrange → Centre on bed** puts the selection in the middle). If it ends *(fill overscan…)*, a
fill at the very edge runs past it: move it in a little, or lower the layer's overscan. Bigger
than the bed? Mark it in sections (see [Work area](work-area.md)).

### A setting is out of range

*Layer "Cut": Speed must be between 2 and 15000 mm/s (got 20000 mm/s)*, and the like for power,
passes, pulse width or frequency. Change that layer's setting (see
[Layer settings](layer-settings.md)). A few others:

- *On highly reflective material, … kHz at … ns is kept out of to protect the laser source…*: use
  the frequency it suggests.
- *Image "…" at … dpi needs … million dots. Lower the resolution or make it smaller.*: a picture
  is too big for its resolution.
- *The focus can move at most 10 mm either way…* or *Stepping down … lowers the head … in total;
  the limit is 10 mm.*: lower the focus offset or step-down.

### Serial numbers and spreadsheets

*The serial number … is past the last one (…). Change it in Variables.*, *All … rows of … have been
marked. Pick a row in Variables to carry on.*, *The design uses {row} but no spreadsheet is
loaded.*, *"{name}" isn't a field.* The text and codes that change on every run can't be filled in.
Fix it in **Home → Variables** (see [Drawing](drawing.md)).

### The head would come too close

*Can't send: Step-down and focus offsets would lower the head 18 mm, but it's only 20.0 mm from the
material. Reduce them.*

A layer's step-down and focus offset together would bring the head within 3 mm of the material.
Lower them, or check the focus distance is right for this material (auto-focus).

### The upload is refused

The machine takes a new job only when it's ready. What it says, and what to do:

| Message | Do |
|---|---|
| *A job is already loaded on the machine, waiting for its start button.* | Start it, or cancel it (**Stop** in the job bar, or long-press the machine's button), then send again. Normally **Ship It** replaces it by itself. |
| *The machine is running a job.* | Wait for it to finish, or stop it. |
| *A job is paused on the machine.* | **Continue** or **Stop** it. |
| *The machine is asleep.* | Press its button to wake it. |
| *The machine is measuring or focusing.* | Wait for it to finish. |
| *The machine is framing (tracing the outline).* | **Stop framing**, then try again. |
| *The machine is reporting an error.* | Check the machine's light and **Settings → Machine**, then send again. |
| *The machine is still showing the last job as finished.* | Press the machine's button once, then send again. |

PhotonCanvas tries to put a finished machine back to *Ready* before uploading; whether the F2 Ultra
accepts that is still to be checked on the machine, hence the last line.

### Upload failed

**Upload failed** with a reason, e.g. *Upload of tmp.xf: the machine stopped asking for data*. The
connection dropped or the machine stopped answering partway. Check the connection (the status pill)
and send again.

## Checks that need attention

### A layer is left out

*Layer "Photo" is a Picture layer, which only engraves pictures, so its 2 shapes won't be done.* or
*Layer "Cut" is a Cut layer, so its picture won't be engraved.* In the confirmation it's first,
under **NOT IN THIS JOB**.

Pictures only engrave on Picture layers; outlines only on Cut, Score and Fill layers. Change the
layer's mode, move the shapes to a layer of the right kind, or trace the picture into outlines (see
[Layers](layers.md) and [Pictures](pictures.md)).

### A layer is hidden

*Layer "…" is hidden, so it will not be processed.* Hidden layers aren't sent. Show it (the eye in
the Layers panel) if it should be.

### Two shapes on top of each other

*"…" and "…" are the same size and in the same place, so both will be processed.* Usually an old
copy left under an edited one: it would be marked twice. Delete one (ungroup first if they're
grouped).

### A layer runs after the cut

*"Engrave" runs after the cut layer "Cut". Parts may move or drop before it is processed; consider
moving it above.* Once a part is cut free it can shift or fall. Layers run from the top of the
[Layers](layers.md) panel down: move the cut layer below the others so it runs last.

### Open paths can't be filled

*Layer "…": 3 open path(s) can't be filled and were skipped.* A fill needs closed shapes. Close
them (**Home → Tidy lines** closes nearly-closed ones), or put them on a Cut or Score layer.

### The pause after each path adds hours

*Pausing 2 s after each of 4,812 paths adds about 2 h 40 min.* The pause after each path (in the
job bar) is per path, and per pass. Lower it, set it to 0, or use **Spread the heat** on that layer
instead, which waits only when it must (see [Spread the heat](heat-order.md)).

If it adds *Most are tiny pieces: if the drawing came in broken up, Outline → Tidy lines joins them
into whole shapes first.*, see the next section.

### An imported drawing is in many tiny pieces

Some files (DXF especially) come in as thousands of short separate lines instead of whole shapes.
The job has far more paths than it should, it jumps about, fills skip the "open" shapes, and the
pause after each path multiplies.

Select the drawing and use **Home → Tidy lines**: it joins ends that nearly meet, closes
nearly-closed shapes and deletes lines lying on top of each other. It asks how close ends must be
to join (0.05 mm to begin with). See [Editing shapes](editing-shapes.md).

## Thin sheet warps

Thin metal and plastic sheet warps when the heat of many cuts builds up in one place. Set the cut
layer's **Order** to **Spread the heat**: every pass of every path is cut where the sheet is
coolest, long paths in short pieces, and the job waits only when the whole sheet is too warm. See
[Spread the heat](heat-order.md).

If **Checks** says *one cut alone warms the sheet about +… °C, more than the +… °C allowed, so the
job waits until each spot is nearly cold*: lower the power, go faster, cut shorter pieces or allow
more warming (**Keep under +°C**).

A simpler fix for a few parts: a pause after each path in the job bar (see
[Passes and pauses](running-jobs.md#passes-and-pauses)).

## The job is slow to build

The job bar takes a while to update, **Working out the heat order…** shows under the timeline, or
**Ship It** says **Shipping it…** for a long time.

A layer that spreads the heat works out the sheet's warming for every cut, which takes time on a
design with thousands of holes, and much more with **Finer heat model (much slower to plan)**
ticked (about 16 times as long). It starts again after every change. Untick the finer model while
you're still editing, and tick it just before shipping. Big pictures at high resolution also take a
moment.

## Framing

- **Can't frame** with a reason: the job can't be built; see **Checks**.
- Framing doesn't start, with a message such as *The machine is running a job…*: the machine must
  be *Ready* or *Job finished*, or have a job loaded (framing clears it).
- *Couldn't stop framing: … The machine's button also stops it.*: press the machine's button.
- *Couldn't move the frame: …*: the frame didn't follow the design. Stop framing and start again.
- With a GRBL machine, frame from its sender window instead.

## While a job runs

### The job doesn't start

After **Ship It** the machine waits for its start button: nothing moves until you press it. The
job bar says **Loaded – press start**. The button won't start it with the lid open unless **Allow
starting with the lid open** is on (**Settings → Machine**); close the lid. Check the safety key and
the emergency stop too (**Device settings… → Interlocks**).

### The time left is off

The estimate leaves out the head's acceleration, so real jobs run a little longer. PhotonCanvas
learns how much from every finished job over 10 seconds, so the time left gets better. Once the
machine reports its own progress (past 2%), the time left comes from that.

### Pause, Continue or Stop show an error

The message shows in the caption for a moment. The connection may have dropped: use the machine's
button (a short press pauses and carries on, a long press while paused cancels).

### A machine alarm

**Machine alarm** pops up for the emergency stop, the flame alarm, the machine being moved or
tilted, and the MOPA laser overheating, saying what to check. Do that before anything else. See
[Alarms and power cuts](machine.md#alarms-and-power-cuts).

### A power cut

When you connect again, **Carry on after the power cut?** offers to carry on where the job stopped.
Check the material hasn't moved, choose **Carry on**, and press the machine's button. Or
**Forget the job**. Not yet tried on the machine.

## Camera

### The camera isn't calibrated

**Calibrate first**: *The camera needs calibrating before its picture can be placed accurately on
the work area.*, or *The camera isn't calibrated yet (Camera tab → Calibrate).*, or *Not calibrated
yet* on the Settings window's Machine page. **Photo** stays grey until it is.

Calibrate once for each material height you use: **Camera → Calibrate** (about 5 minutes and a
scrap sheet). See [Camera](camera.md).

### The picture is dark or old

- During a job the machine turns its lights off, so the cameras are dark.
- The first photo after connecting can be too bright; PhotonCanvas retakes it until the exposure
  settles.
- *Connect to the machine to take a photo.*: connect first.
- *Live view stopped: …*: three pictures in a row failed. Turn **Live** on again.
- The **Lights** on the machine panel set how bright the bed is.

## Focus

### The machine is busy

*The machine is busy (Running a job). Try again when it's ready.* Focusing and moving the head only
work when the machine is *Ready* or *Job finished*. Wait for the job, or stop it.

### Auto-focus stopped

- *Auto-focus was stopped with the machine's button.*: someone pressed it. Try again.
- *Auto-focus didn't finish within 2 minutes.*: check there's material under the head, and try
  again.
- *Auto-focus was interrupted: the machine started homing its Z axis.*: let it finish, then
  focus again.

### The focus height seems off

The distance shown doesn't match the material. Use **Settings → Tools → Home the Z axis**: the
head goes up to find its zero again. Then auto-focus.

## Other problems

### No time-lapse video

*Time-lapse photos saved in … (install ffmpeg to get a video).* PhotonCanvas makes the video with
ffmpeg, which it doesn't come with. Install it so it's on the PATH; the photos are kept meanwhile.

### The console says Not connected

*Not connected: connect the machine first (right end of the ribbon).* Connect, then send again. No
**Console** on the **Laser** tab: tick **G-code console** in **Settings → Equipment**.

### A GRBL machine

- *No answer from GRBL on COM3 at 115200 baud.*: the wrong port or speed, or the controller doesn't
  run GRBL.
- *COM3 is in use by another program (close LightBurn or a terminal).*
- *error:9 — GRBL is locked*: **Unlock ($X)** or **Home ($H)** first.
- *ALARM:1* or *ALARM:2*, or *error:15*: the job goes past the machine's travel. Check the bed size
  and **Where 0, 0 is**, and home it.
- *GRBL machines take flat-bed jobs only (no rotary, conveyor or curved surface).*

See [GRBL machines](machine.md#grbl-machines). GRBL support hasn't been tried on a real machine
yet.

## Try it

- [Connect to the machine](tutorial:mach-connect)
- [Read the job bar](tutorial:job-read-the-bar)
