# Writing the help guide and the tutorials

The guide (Help → Help guide, F1) and the tutorials (Help → Tutorials) are the files in this folder.
They ship next to the program in a `help` folder and are read when the program runs. Every new
feature gets its place in the guide and a tutorial: the tests in `tests/PhotonCanvas.Tests/HelpTests.cs`
fail when a ribbon button, a layer setting or a window has none.

## Files

- `contents.md`: the guide's table of contents, a nested list of links to pages.
- `*.md` (except this one and `contents.md`): the pages.
- `images/*.png`: screenshots and pictures (made by the screenshot harness, see below).
- `tutorials/*.md`: one tutorial per file.

## Pages

A page starts with `# Title`. Sections are `##`, smaller parts `###`. Write for someone who has the
machine in front of them: say what a thing is for, when to use it, how it works and what each
setting does, in plain words with exact names and values. Name controls exactly as the program
shows them, in bold (**Ship It**, **Keep under +°C**), keys as code (`Ctrl+S`).

What the program reads (a small part of Markdown, so pages look the same on GitHub):

| Write | For |
|---|---|
| `#`, `##`, `###` at the start of a line | Headings. Links to a section use GitHub's anchor: lower case, spaces as dashes, punctuation dropped (`## Spread the heat` → `#spread-the-heat`). |
| Lines of text | A paragraph (a blank line ends it). |
| `- item` or `1. item`, two more spaces for a list under an item | Lists. |
| `**bold**`, `*italic*`, `` `Ctrl+S` `` | Emphasis, and keys or names set off like key caps. |
| `[text](page.md)`, `[text](page.md#section)`, `[text](#section)` | Links within the guide. |
| `[text](tutorial:id)` | A link that starts a tutorial. |
| `![Caption](images/name.png)` on a line of its own | A picture with a caption (click to see it full size). |
| `> **Tip:** …`, `> **Note:** …`, `> **Warning:** …`, `> **Safety:** …` | A boxed tip, note, warning or safety note. |
| `\| a \| b \|` rows, `\|---\|---\|` under the first | A table. |
| `<!-- windows: CutTestWindow, CutTestPreview -->` | F1 in those windows opens this page, at the section the line is in. |

Nothing else (no HTML, no nested quotes, no images inside lists).

## Tutorials

A tutorial teaches one feature by doing it, a step at a time. A card shows each step and rings the
control it's about; a step can wait for the user to do something and then move on by itself.

```
# Cut a test grid
<!-- category: Materials and tests -->
<!-- summary: Find the power and speed that cut your material, with a grid of small squares. -->
<!-- page: materials-and-tests.md#cut-test -->
<!-- order: 20 -->
When and why to use it, in two or three sentences (this is the first card).

## Open the cut test
<!-- target: ribbon:Laser/Cut test -->
<!-- wait: window:Cut test -->
Click **Cut test** on the **Laser** tab.

## Choose the speeds
<!-- target: field:Speeds -->
Type the speeds to try, separated by commas.
```

- `category`: one of Getting started, The workspace, Drawing, Editing shapes, Arranging, Importing and exporting,
  Layers and settings, Pictures, Camera, Materials and tests, Running jobs, Work area, Machine and settings, 3D and slices.
- `summary`: one line, what it teaches. `page`: the guide's page (and section) that explains it fully. `order`: its place in its category.
- Each `##` is a step: a short title and one to three short sentences, one action. A list is fine when
  there are a few choices to explain; never a wall of text. Say what to do, then what it does.
- Steps that need a picture, a selection or a connected machine say so first, and say how to get one.

### What a step can point at (`target`)

| Target | Points at |
|---|---|
| `ribbon:Tab/Label` | A ribbon button (its tab is opened). Icon-only buttons by their tooltip: `ribbon:Arrange/Align left edges`. |
| `tab:Name` | A ribbon tab's header. |
| `header:File`, `Save`, `Undo`, `Redo`, `Settings`, `Help` | The buttons along the top. |
| `panel:Layers`, `Selection`, `Layer settings`, `Job`, `Machine` | A panel (unfolded if folded). |
| `canvas` | The work area. |
| `field:Label` | A setting by its name, in whichever window is open (the newest first). Sections it's in are opened. |
| `button:Text` | A button or checkbox by its text (or, for a button whose text changes, how its text starts: `button:Save chosen squares`). |
| `tabitem:Header` | A tab in a window. |
| `text:Words` | Any text containing these words. |
| `window:Title` | A whole window (its title starts with this). |

### What a step can wait for (`wait`)

| Wait | Done when |
|---|---|
| `window:Title` | A window whose title starts with this is open. |
| `closed:Title` | No such window is open any more. |
| `selection` or `selection:N` | Something (N things) is selected, newly: a selection already there when the step began doesn't count. |
| `shapes:+N` | N more shapes than when the step started. |
| `layers:+N` | N more layers. |
| `tool:Name` | That tool is chosen: Select, Nodes, Pan, Text, Pen, Rect, Ellipse, Polygon, Star, Line, Wand, Magnetic, Trim, Scissors, Tabs, Start, Measure, Align2, Eraser. |
| `tab:Name` | That ribbon tab is open. |
| `layer-mode:Mode` | The active layer's mode is Cut, Score, Fill or Image. |
| `zoom` | The view was zoomed. |
| `picture-selected` | A picture is selected. |

Without a `wait` the user presses **Next**. Don't wait for anything a user might reasonably skip.

## Screenshots

`images/` is made by `tools/HelpShots`, which runs the real program headless with a sample design
(nothing touches the real data folder or a machine). From the repository root:

```
dotnet run --project tools/HelpShots -- shots              (retake every picture)
dotnet run --project tools/HelpShots -- shots more         (one group: main, windows, extra or more)
dotnet run --project tools/HelpShots -- shots --keep-new   (keep new pictures no page shows yet)
dotnet run --project tools/HelpShots -- guide              (the help window and a tutorial running)
dotnet run --project tools/HelpShots -- walk [id,id]       (step through tutorials, report missing targets)
```

- `shots` opens every window the ribbon and menus open (each tab), every kind of layer's settings,
  the panels, and (in `MoreShots.cs`) the windows that need something in them first: 3D models in
  the slicing window, a heat-ordered plate, undo steps, versions, templates, jobs. A new window or
  state that needs setting up goes in `MoreShots.cs`.
- New pictures no page shows are deleted after a run unless `--keep-new` is given; every picture in
  `images/` must be on a page (`HelpTests` checks).
- `walk` clicks what each step says (ribbon and top buttons, the menu item a step names in bold, a
  button that opens or closes a window, tabs, list pages). Steps needing a connected machine, a
  camera photo, a 3D model or a burned test are expected to be reported.

Name pictures after what they show (`laser-cut-test.png`). Use a screenshot where seeing it helps: a
window full of settings, a panel, a result on the work area. Not for every button, and not for a
plain message or a one-line prompt.

## When a feature is added or changed

1. Describe it on its page (bold names exactly as shown; every setting, what it does, its range).
2. If it's a window, add its class to a `<!-- windows: … -->` line.
3. Add or update a tutorial, with targets that exist.
4. Retake screenshots that show it (`tools/HelpShots`), and put new ones on the page.
5. Run `walk` on its tutorials, and the tests: `HelpTests` checks links, pictures (every one used),
   targets and that every button, setting and window is covered. `python tools/helpcheck.py` checks
   the same without building.
