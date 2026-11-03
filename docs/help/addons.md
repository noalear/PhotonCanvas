# Add-ons
<!-- windows: AddonsWindow -->

Many of PhotonCanvas's features are **add-ons**: extra tools that can be turned on or off, or
installed and removed, while the program is running. Turning one off takes its buttons, commands,
menu items and help pages away straight away; turning it on brings them back. Nothing else changes,
and designs keep anything an add-on saved in them even while it's off, so turning it back on picks
up where you left off.

Driving the machine, making and sending jobs, drawing, layers, files, the material library and the
camera are part of the program itself and are always there.

## The Add-ons window

Open it from **View → Show → Add-ons**, or **Settings → Add-ons → Add-ons…**.

The add-ons are listed by kind (Make, Picture, Camera, Laser, Arrange, Other). Each shows its name,
version, whether it **comes with PhotonCanvas** or was **installed**, and what it does.

- **On / Off**: turns the add-on on or off at once. The program remembers the choice for next time.
  An add-on that needs another one turns that one on too, and turning off an add-on also turns off
  the ones that need it.
- **Remove** (only for add-ons you installed): turns it off and deletes it from this computer. The
  ones that come with PhotonCanvas can be turned off but not removed.
- **Install from file…**: adds an add-on from a `.pcaddon` file (a zip of the add-on's folder), or updates one you already have. It's turned on once installed.
- **Open add-ons folder**: where the add-ons you install are kept.

Add-ons that come with PhotonCanvas are updated with it. An add-on you installed yourself is kept
up to date when its maker says where its new versions are published (on GitHub): PhotonCanvas looks
when it checks for its own new versions (see [Updates](machine.md#updates)), and puts a newer one
in straight away, stopping and starting it (its windows close). It stays on or off as you had it.
With **Put new versions in by themselves** off, you're asked first.

If an add-on can't start, a ⚠ line under it says why (for example, it was made for a newer
PhotonCanvas, or it needs another add-on that isn't installed). The rest of the program carries on
as normal.

> **Safety:** Add-ons can't start the laser by themselves: every job still goes through **Ship It**
> and waits on the machine until you press its button. Only install add-ons from people you trust.

### Turning one off from where it is

Right-click anything an add-on made: its ribbon button, a setting in its window, or a shape it put in
the design. **Disable add-on: …** at the end of the menu turns that add-on off straight away (add-ons
that need it go off too, after asking). The rest of the program carries on; turn it back on here.
See [Right-click for help](workspace.md#right-click-for-help).

## Help for add-ons

Each add-on brings its own pages, listed under **Add-ons** at the end of the guide's contents, and
its own tutorials. They're only there while the add-on is on. Right-clicking anything an add-on made
offers **See tutorial** and **See help** for it.

## Tutorial

- [Turn an add-on off and on](tutorial:addons)
