# PhotonCanvas add-ons (API version 1)

Features that aren't needed to drive the machine are **add-ons**: each one can be turned on, off,
installed or removed from **Settings → Add-ons** while the program runs, without restarting. This
page is the contract an add-on is written against. The code is in `src/PhotonCanvas.App/Addons/`
(`AddonApi.cs` is the whole interface) and `src/PhotonCanvas.Doc/Extensions/`.

What stays in the program (never an add-on): connecting to and driving machines, the job builder and
G-code, the canvas, layers, drawing and editing tools, files and imports, the material library, the
camera and its calibration, settings and the help guide.

## How an add-on is put together

One folder per add-on under `src/Addons/`:

```
src/Addons/Papercraft/
  PhotonCanvas.Addon.Papercraft.csproj
  addon.json
  PapercraftAddon.cs          the IAddon class
  ...                         its windows and its own geometry
  help/                       its pages for the guide (optional, same format as docs/help)
    papercraft.md
    tutorials/papercraft.md
```

The project file is short; `src/Addons/Directory.Build.props` (target framework) and
`src/Addons/Directory.Build.targets` do the rest (they reference the program for compiling only, and
after every build copy the add-on into `src/PhotonCanvas.App/bin/<Configuration>/net10.0/addons/<id>/`,
where the program finds it; `tools/publish.sh` puts them in the published program the same way):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <AddonId>papercraft</AddonId>
  </PropertyGroup>
</Project>
```

Don't add the project to `PhotonCanvas.slnx`, the test project or `tools/HelpShots`: the test
project and HelpShots reference every `src/Addons/*/*.csproj` by folder, so a solution build builds
each add-on (in the solution's configuration) and the tests and screenshots can use it. Keeping
those files untouched is what stops add-on branches conflicting with each other. An add-on may use anything
public in Core, Doc, Device and App (App's `UiKit` helpers included) and its own NuGet packages; it
must not copy the program's assemblies (the targets file makes sure of that).

### addon.json

```json
{
  "id": "papercraft",
  "name": "Fold-up papercraft",
  "version": "1.0.0",
  "description": "Unfold a 3D model into one sheet with cut lines, fold lines and glue tabs.",
  "category": "Make",
  "apiVersion": 1,
  "requires": [],
  "onByDefault": true
}
```

`id`: lower case letters, digits, `-`, `_`, `.`; unique. `category` groups the Add-ons window: use
Make, Picture, Camera, Laser, Arrange or Other. `requires` lists other add-ons' ids whose services you
use (they start first, stop after you). Optional: `assembly` (default: the only `PhotonCanvas.Addon.*.dll`),
`entry` (default: the only public `IAddon` class) and `updates` (below).

### Updates

Built-in add-ons are updated with the program (each published version of PhotonCanvas carries
them; see `.github/workflows/release.yml`). An add-on installed from a `.pcaddon` file can keep
itself up to date: put `"updates": "owner/repo"` in its addon.json, and publish each new version as
a GitHub release of that repository (tag `v1.2.0`, the same version as in addon.json) with the
`.pcaddon` attached (named after the id, e.g. `grain-1.2.0.pcaddon`, when there's more than one)
and, ideally, a `SHA256SUMS` file (`sha256sum *.pcaddon > SHA256SUMS`): when it's there the
download must match it. Whenever the program checks for its own updates it looks at the latest
release; a newer version is installed while the program runs (the add-on is stopped and started
again, staying on or off as the person had it). A private repository works with the GitHub access
key from Settings → Updates.

### The add-on class

```csharp
using PhotonCanvas.App.Addons;

public sealed class PapercraftAddon : IAddon
{
    IAddonHost _host = null!;

    public void Start(IAddonHost host)
    {
        _host = host;
        host.AddRibbonButton(new RibbonButton("Home", "Bring in", "Papercraft", "IcDepth", Open)
        {
            Tip = "Unfold a 3D model into a flat sheet that folds back up into it",
        });
        host.AddCommand(new AddonCommand("papercraft.open", "Shapes", "Fold-up papercraft…", "", Open));
    }

    void Open() => _ = _host.Guard(() => new PapercraftWindow(_host).ShowDialog(_host.Owner));

    public void Stop() { /* close your windows, stop timers; buttons etc. go by themselves */ }
}
```

## The host (`IAddonHost`)

Everything added through the host is taken away again when the add-on stops: you never need to
remove your own buttons. `Start` and `Stop` run on the UI thread.

| Member | What for |
|---|---|
| `Document` | The open design: `State` (a `DocState`), `Selection`, `ActiveLayer`, `Commit(label, newState, select)` = one undo step; `BeginEdit/UpdateEdit/EndEdit` for live dragging. Build new states with `Ops` (`Ops.AddShapes`, `Ops.AddLayer`, …). |
| `DocumentChanged` | After any change, undo/redo, or another file opened. |
| `Machine`, `MachineChanged` | The chosen machine's profile (work area `FieldW/FieldH`, lasers, limits). |
| `Library`, `RefreshLibrary()` | The material library (recipes, kerf, slot fit…); after changing and saving it, `RefreshLibrary()` updates the program's material lists. |
| `AppSettings`, `SaveAppSettings()` | The program's settings (read; write only with care). Keep your own settings in `DataFolder`. |
| `SelectedPicture` | The picture, when exactly one is selected. |
| `Photo`, `PhotoChanged`, `TakePhotoAsync()` | The overview camera photo of the bed (`RasterImage`, bed `Area`, `MmPerPx`); taking one doesn't move anything. |
| `CloseUpPhotoAsync()`, `CloseUpSees()` | A close-up photo laid straight on the bed, and where the close-up camera looks. |
| `Lights`, `SetLights()` | The machine's lights. |
| `IsConnected` | Is a laser connected. |
| `FocusDistanceMm`, `CameraCalibratedFor(d)`, `PhotographAreaAsync(area, mmPerPx, d)` | The measured focus distance; is the overview camera calibrated there; a fresh straightened photo of part of the bed (pointers off, nothing moves). |
| `LastSentJob` | The last `BuiltJob` sent from this session, and when. |
| `JobStarted`, `JobEnded` | A job started running on the machine (its name), and stopped (true = finished). Any job, also one started from the machine. |
| `JobFinished` | A job sent from this session finished on the machine (argument: the design file it came from). Tests open their results from it. |
| `MachinePanel` | The program's machine connection itself, for what the members above don't cover (close-up camera placement, the red pointer, measuring). It can move the head and fire: only from a button that plainly says so. Its members may change between versions. |
| `Owner` | The main window, as owner for your dialogs. |
| `Status(text)`, `Guard(work)` | A status-bar line; run work showing errors as a message, not a crash. |
| `ShowHelp(file, anchor)` | Open a page of your help (or the program's). |
| `InvalidateCanvas()`, `FitWorkArea()`, `RefreshCommands()` | Redraw the canvas; zoom out to the whole work area; re-ask buttons' enabled/checked/visible tests. |
| `ActivateLayer(id)` | Make a layer the active one and pick it in the Layers panel (after adding it). |
| `Folder`, `DataFolder`, `Manifest` | Your installed folder (read-only), a folder for your own files (kept across updates and removal), your addon.json. |

### Adding things

| Call | Adds |
|---|---|
| `AddRibbonButton(RibbonButton)` | A ribbon button in a tab's group (made if missing, and taken away again when your last button goes). `Large`, `Tip`, `Enabled`, `Checked` (a toggle), `Visible`, `GroupAfter` (where a new group goes: `"Tests|Materials"` = after the first of those there is). A group made for add-ons hides while none of its buttons show. `Order` sorts add-ons' buttons within a group (after the program's own), so the order doesn't depend on which add-on started first. `Icon` = a program icon key from `src/PhotonCanvas.App/Icons.axaml` or SVG path data in a 24×24 box. |
| `AddCommand(AddonCommand)` | A command in the shortcut list and command search; `Keys` like `"Ctrl+Alt+P"` gives it a shortcut (the person can change it). |
| `AddContextMenuItem(ContextMenuEntry)` | An item on the canvas's right-click menu (`Visible`, `Enabled`, `Gesture`). |
| `ShowCloseUpAreaWhile(when)` | The outline of where the close-up camera looks, on the canvas while `when()` says so (e.g. while your test is in the design). |
| `AddCanvasOverlay(ICanvasOverlay)` | Drawing on the canvas: `UnderShapes` (over the camera picture, world mm), `OverShapes` (world mm), or `Screen` (pixels). Keep it fast. |
| `AddToSlot(slot, make)` | Your panel inside a window's named slot (below). |
| `AddJobHook(IJobHook)` | A step in making the job (below). |
| `Publish<T>(service)` / `Find<T>()` | Offer an interface to other add-ons / use one another add-on offers (list it under `requires`). |

### Window slots

A window with a slot calls `AddonSlots.Fill(slot, panel, window, data, changed)` when it opens; your
`make(SlotContext)` returns a control (or null). `SlotContext.Data` is what the window passes, and
`SlotContext.Changed()` tells the window to recompute. Slots there are now:

| Slot | Where | `Data` |
|---|---|---|
| `properties.picture` | Selection panel, Picture settings, under the program's buttons (filled again when another picture is selected or an add-on comes or goes) | the selected `ImageShape` |
| `stackslice.stacked` | 3D to slices, under the "Stacked layers" settings | `IStackSliceSlot` (read the options, add an `ISliceModifier`); see the slicer add-on |

Ask the add-ons thread for a new slot when you need one in a program window; add slots of your own
to your own windows the same way.

### Job hooks (`PhotonCanvas.Doc.Extensions.IJobHook`)

Run every time a job is made (sending, framing, preview, time estimate), often on a background
thread, so no UI in them.

- `DocState BeforeBuild(DocState state, JobOptions options)`: return a changed **copy** of the
  design to build from (the person's document isn't touched): split a fill into regions of
  different power, swap a picture for a tone-adjusted one, and so on. Return `state` to leave it.
- `IEnumerable<string> Check(DocState state, JobOptions options)`: extra warnings for the job's checks.

A hook that throws is skipped for that job, with a warning saying so.

### Your notes in the design

`Shape.AddonData` and `DocState.AddonData` are string → string maps saved in the `.pcanvas` file.
Start each key with your id (`"grain.map"`); store JSON in the value if you need structure. Use
`shape.WithAddonData(key, value)` (null removes) and `state with { AddonData = ... }`, then commit.
They're kept untouched while your add-on is off or not installed, copied with the shape, and kept
when a shape is rebuilt.

### Help pages and tutorials

Put pages in your `help/` folder in the guide's format (`docs/help/AUTHORING.md`). You may link to the program's pages and to the pages of add-ons you list under `requires`. They join the
guide under **Add-ons** while you're on, and are searched with the rest. Link to your own pages by
file name; images go in `help/images/`. A `<!-- windows: PapercraftWindow -->` line makes F1 in that
window open the page. Every add-on needs at least one page and one tutorial.

### 3D views of a model put together

Don't draw a 3D model by sorting its faces far to near and painting them over each other: with long
faces beside small ones, or pieces slotted into each other, faces pop in front of or behind each other
as the model turns. Use the program's painter (`PhotonCanvas.App.View3D`), which draws with a depth
buffer so the nearest face always shows:

```csharp
readonly Scene3D _scene = new();
readonly Model3DPainter _painter = new();

// When the model changes: each flat piece, from its outline (u, v), where it sits and how thick it is.
_scene.Clear();
_scene.AddSlab(outline, new Frame3(origin, u, v), -t / 2, t / 2, style, etched, scoredLines, group: 0);

// In Render: draft while dragging (quicker); groups can be hidden (e.g. a floor lifted off).
_painter.Draw(ctx, this, _scene, Camera3D.YUp(yaw, pitch), UiKit.Brush("Bg2"), hiddenGroups, draft: dragging);
```

A `SlabStyle` says the colours (`SlabStyle.Lit` lights one colour), the edge lines, and the colours of
what's etched and scored. A face colour that isn't fully opaque makes a see-through ghost (the stairs
maker's walls). The bridge, stairs, furniture, scatter, game kit, building and roof makers all use it.

## Checks

`tests/PhotonCanvas.Tests/HelpTests.Addons.cs` runs for every folder under `src/Addons` with an
addon.json: it needs a page and a tutorial in `help/`, every link and picture to exist, every
`new RibbonButton("Tab", "Group", "Label", …)` to be explained (as **Label**) and taught by a tutorial
(`ribbon:Tab/Label`), and every window to be claimed by a page. The program's own guide must not link
into an add-on's pages (they come and go). To try the real thing headless, set `PHOTONCANVAS_ADDONS=1`
(add-ons are off in screenshot/test windows otherwise) and `PHOTONCANVAS_ADDONS_DIR` to a folder of
built add-ons.

## Rules

- **Never move the head or fire the laser** unless the person pressed a button that plainly says it
  will. Jobs only ever go through the program's own Ship It (upload, `autoStart: 0`).
- Never put xTool-derived code or connection keys in an add-on.
- Clean up in `Stop`: close your windows, cancel background work, unhook events you hooked on the
  program's objects directly (the host's events unhook themselves). Avoid static fields that point
  at program objects, so the add-on can be unloaded from memory. (The program itself forgets your
  ribbon buttons and takes your window and control classes out of Avalonia's per-type caches.)
- Don't register your own Avalonia properties (`AvaloniaProperty.Register…`): Avalonia keeps them,
  and so the add-on, in memory until the program closes. Plain C# properties are fine.
- With System.Text.Json, don't use `JsonSerializerOptions.Default` or a plain `new JsonSerializerOptions()`
  (or options equal to them): equal options share one cache that the program keeps, and it would
  hold your types. Give your options their own resolver,
  `TypeInfoResolver = new DefaultJsonTypeInfoResolver()`, and keep them in a field of your own.
- To check yours leaves memory: turn it off and look in the log. "still in memory after stopping"
  means something still points into it.
- Build windows in code like the rest of the program (no `.axaml` in add-ons).
- Don't change program files beyond a slot or hook you need; ask the add-ons thread for those.

## Installing from a file

**Settings → Add-ons → Install from file…** takes a `.pcaddon` file (a zip of the add-on's folder:
addon.json, its .dll files, help/) or a folder. Installed add-ons live in the data folder's `addons`
folder; ones that come with the program sit in `addons` beside it and can be turned off but not
deleted. One installed with the same id as a built-in one replaces it.
