"""Check the help guide and tutorials (docs/help) against the program's source, without building it.

    python tools/helpcheck.py                  # everything
    python tools/helpcheck.py --area Laser     # also: which buttons of the Laser tab aren't documented / have no tutorial
    python tools/helpcheck.py --files a.md b.md  # problems in these pages / tutorials only

Checks: every link goes to a page and section that exist, every picture exists, every tutorial has
a category, summary and steps, and every step's target and wait are ones the program knows and
point at things that exist (ribbon buttons by tab and label, settings and buttons by their text).
Lists ribbon buttons, layer settings and windows that the guide doesn't mention yet.
The same checks run in tests/PhotonCanvas.Tests/HelpTests.cs.
"""
import os, re, sys, glob

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
HELP = os.path.join(ROOT, "docs", "help")
APP = os.path.join(ROOT, "src", "PhotonCanvas.App")

TARGET_KINDS = {"ribbon", "tab", "header", "panel", "canvas", "field", "button", "tabitem", "text", "window"}
WAIT_KINDS = {"window", "closed", "selection", "shapes", "tool", "tab", "layer-mode", "layers", "zoom", "picture-selected"}
HEADERS = {"file", "save", "undo", "redo", "settings", "help"}
PANELS = {"layers", "selection", "layer settings", "job", "machine"}
TOOLS = {"select", "rect", "ellipse", "polygon", "star", "line", "pen", "text", "nodes", "pan", "wand", "magnetic", "measure", "trim", "align2", "scissors", "tabs", "start", "eraser"}
MODES = {"cut", "score", "fill", "image"}
CATEGORIES = ["Getting started", "The workspace", "Drawing", "Editing shapes", "Arranging", "Importing and exporting", "Layers and settings",
              "Pictures", "Camera", "Materials and tests", "Running jobs", "Work area", "Machine and settings", "3D and slices"]


def read(p):
    with open(p, encoding="utf-8") as f:
        return f.read()


def anchor(h):
    out = []
    for c in h.strip().lower():
        if c.isalnum() or c in "-_":
            out.append(c)
        elif c == " ":
            out.append("-")
    return "".join(out)


def app_sources():
    return {p: read(p) for p in glob.glob(os.path.join(APP, "*.cs"))}


def ribbon_commands(src):
    """{'Tab/Label': group title} from MainWindow's ribbon."""
    text = src[os.path.join(APP, "MainWindow.axaml.cs")]
    tabs, groups, cmds = {}, {}, {}
    for m in re.finditer(r'var (\w+) = _ribbon\.Tab\("([^"]+)"', text):
        tabs[m.group(1)] = m.group(2)
    for m in re.finditer(r'var (\w+) = (\w+)\.Group\("([^"]+)"', text):
        if m.group(2) in tabs:
            groups[m.group(1)] = (tabs[m.group(2)], m.group(3))
    for m in re.finditer(r'\b(\w+)\.(Large|Small|LargeToggle|SmallToggle)\("([^"]+)"', text):
        if m.group(1) in groups:
            cmds[f"{groups[m.group(1)][0]}/{m.group(3)}"] = groups[m.group(1)][1]
    for m in re.finditer(r'Tool(?:Large|Small)\((\w+), "([^"]+)"', text):
        if m.group(1) in groups:
            cmds[f"{groups[m.group(1)][0]}/{m.group(2)}"] = groups[m.group(1)][1]
    for m in re.finditer(r'(\w+)\.Icons\(\d+,(.*?)\);', text, re.S):
        if m.group(1) in groups:
            for t in re.finditer(r'\("Ic\w+", "([^"]+)"', m.group(2)):
                cmds[f"{groups[m.group(1)][0]}/{t.group(1)}"] = groups[m.group(1)][1]
    return cmds


def layer_fields(src):
    text = src[os.path.join(APP, "LayerEditor.cs")]
    names = set(re.findall(r'Field\("([^"]+)"', text)) | set(re.findall(r'Labelled\("([^"]+)"', text))
    names |= set(re.findall(r'new CheckBox \{ Content = "([^"]+)"', text))
    return sorted(names)


def windows(src):
    out = {}
    for p, t in src.items():
        for m in re.finditer(r'class (\w+) : Window\b', t):
            out[m.group(1)] = os.path.basename(p)
    out.pop("MainWindow", None)
    return out


def strings(src):
    """Every string literal in the program's interface code (for checking targets by their text)."""
    out = set()
    for t in src.values():
        for m in re.finditer(r'\$?"((?:[^"\\]|\\.)*)"', t):
            out.add(m.group(1))
    # And the labels written in XAML.
    for p in glob.glob(os.path.join(APP, "*.axaml")):
        for m in re.finditer(r'(?:Text|Content|Header|Watermark|Title)="([^"]*)"', read(p)):
            out.add(m.group(1))
    return out


def parse_blocks(text):
    """(headings, links, images, directives) of a page or tutorial."""
    heads, links, imgs, dirs = [], [], [], []
    for line in text.replace("\r\n", "\n").split("\n"):
        s = line.strip()
        d = re.match(r'^<!--\s*([\w-]+)\s*:\s*(.*?)\s*-->$', s)
        if d:
            dirs.append((d.group(1).lower(), d.group(2)))
            continue
        if s.startswith("#"):
            lvl = len(s) - len(s.lstrip("#"))
            heads.append((lvl, s[lvl:].strip()))
            continue
        im = re.match(r'^!\[([^\]]*)\]\(([^)\s]+)\)$', s)
        if im:
            imgs.append(im.group(2))
            continue
        for l in re.finditer(r'\[([^\]]+)\]\(([^)]+)\)', s):
            links.append(l.group(2))
    return heads, links, imgs, dirs


def main(argv):
    area = None
    only = None
    if "--area" in argv:
        area = argv[argv.index("--area") + 1]
    if "--files" in argv:
        only = set(os.path.basename(a) for a in argv[argv.index("--files") + 1:])
    src = app_sources()
    cmds = ribbon_commands(src)
    lits = strings(src)
    pages = {os.path.basename(p): read(p) for p in glob.glob(os.path.join(HELP, "*.md")) if os.path.basename(p) not in ("AUTHORING.md",)}
    tuts = {os.path.basename(p): read(p) for p in glob.glob(os.path.join(HELP, "tutorials", "*.md"))}
    anchors = {}
    for name, text in pages.items():
        heads, _, _, _ = parse_blocks(text)
        anchors[name] = {anchor(h) for lvl, h in heads if lvl >= 2}
    tut_ids = {}
    for name, text in tuts.items():
        _, _, _, dirs = parse_blocks(text)
        tid = next((v for k, v in dirs if k == "id"), name[:-3])
        tut_ids[tid] = name
    problems = []

    def check_link(where, target):
        if target.startswith(("http://", "https://")):
            return
        if target.startswith("tutorial:"):
            if target[9:] not in tut_ids:
                problems.append(f"{where}: no tutorial '{target[9:]}'")
            return
        page, _, anc = target.partition("#")
        page = os.path.basename(page) if page else where.split(":")[0]
        if page not in pages:
            problems.append(f"{where}: link to missing page '{target}'")
        elif anc and anc not in anchors.get(page, set()):
            problems.append(f"{where}: link to missing section '{target}'")

    for name, text in pages.items():
        if only and name not in only:
            continue
        heads, links, imgs, dirs = parse_blocks(text)
        if not heads or heads[0][0] != 1:
            problems.append(f"{name}: doesn't start with a # title")
        for l in links:
            check_link(name, l)
        for i in imgs:
            if not os.path.exists(os.path.join(HELP, i)):
                problems.append(f"{name}: missing picture {i}")
    for name, text in tuts.items():
        if only and name not in only:
            continue
        heads, links, imgs, dirs = parse_blocks(text)
        keys = {k for k, _ in dirs}
        for need in ("category", "summary"):
            if need not in keys:
                problems.append(f"tutorials/{name}: no {need}")
        cat = next((v for k, v in dirs if k == "category"), None)
        if cat and cat not in CATEGORIES:
            problems.append(f"tutorials/{name}: unknown category '{cat}'")
        steps = [h for lvl, h in heads if lvl == 2]
        if len(steps) < 2:
            problems.append(f"tutorials/{name}: fewer than 2 steps")
        for k, v in dirs:
            if k == "page":
                check_link(f"tutorials/{name}", v)
            if k == "target":
                kind, _, val = v.partition(":")
                kind = kind.strip().lower(); val = val.strip()
                if kind not in TARGET_KINDS:
                    problems.append(f"tutorials/{name}: unknown target kind '{v}'")
                elif kind == "ribbon" and val not in cmds:
                    problems.append(f"tutorials/{name}: no ribbon button '{val}'")
                elif kind == "tab" and val not in {c.split('/')[0] for c in cmds}:
                    problems.append(f"tutorials/{name}: no ribbon tab '{val}'")
                elif kind == "header" and val.lower() not in HEADERS:
                    problems.append(f"tutorials/{name}: no header button '{val}'")
                elif kind == "panel" and val.lower() not in PANELS:
                    problems.append(f"tutorials/{name}: no panel '{val}'")
                elif kind in ("field", "button", "tabitem", "window") and not any(val.lower() == s.lower().strip().rstrip(":…") or s.lower().startswith(val.lower()) for s in lits):
                    problems.append(f"tutorials/{name}: no text '{val}' in the program for target '{v}'")
                elif kind == "text" and not any(val.lower() in s.lower() for s in lits):
                    problems.append(f"tutorials/{name}: no text containing '{val}' in the program")
            if k == "wait":
                kind, _, val = v.partition(":")
                kind = kind.strip().lower(); val = val.strip()
                if kind not in WAIT_KINDS:
                    problems.append(f"tutorials/{name}: unknown wait '{v}'")
                elif kind == "tool" and val.lower() not in TOOLS:
                    problems.append(f"tutorials/{name}: unknown tool '{val}'")
                elif kind == "layer-mode" and val.lower() not in MODES:
                    problems.append(f"tutorials/{name}: unknown layer mode '{val}'")
                elif kind == "tab" and val not in {c.split('/')[0] for c in cmds}:
                    problems.append(f"tutorials/{name}: no ribbon tab '{val}'")
        for l in links:
            check_link(f"tutorials/{name}", l)

    # Coverage: what the guide doesn't mention yet.
    raw = "\n".join(pages.values())
    tut_raw = "\n".join(tuts.values())
    undocumented = [c for c in cmds if f"**{c.split('/', 1)[1]}**" not in raw and f"`{c.split('/', 1)[1]}`" not in raw]
    untaught = [c for c in cmds if f"ribbon:{c}" not in tut_raw]
    fields = [f for f in layer_fields(src) if f"**{f}**" not in raw and f"`{f}`" not in raw]
    wins = windows(src)
    mapped = set()
    for text in pages.values():
        for k, v in parse_blocks(text)[3]:
            if k == "windows":
                mapped |= {w.strip() for w in v.split(",")}
    unmapped = [w for w in sorted(wins) if w not in mapped]

    for p in problems:
        print("PROBLEM", p)
    if area:
        sel = [c for c in cmds if c.startswith(area + "/")]
        print(f"\n{area}: {len(sel)} buttons")
        for c in sel:
            flags = []
            if c in undocumented:
                flags.append("not in the guide as **label**")
            if c in untaught:
                flags.append("no tutorial step targets it")
            print(f"  {c:45s} {'; '.join(flags) if flags else 'ok'}")
    if not only:
        print(f"\nRibbon buttons not in the guide: {len(undocumented)}")
        for c in undocumented:
            print("  ", c)
        print(f"Ribbon buttons no tutorial targets: {len(untaught)}")
        for c in untaught:
            print("  ", c)
        print(f"Layer settings not in the guide: {len(fields)}")
        for f in fields:
            print("  ", f)
        print(f"Windows no page claims (<!-- windows: -->): {len(unmapped)}")
        for w in unmapped:
            print("  ", w, f"({wins[w]})")
    print(f"\n{len(problems)} problem(s); {len(pages)} pages, {len(tuts)} tutorials.")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
