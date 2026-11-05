# Phone remote

Watch and run the laser from your phone: see what the machine is doing and how far the job has got,
take pictures with its cameras, pause, continue and stop, pick a design to load, and start it. The
phone talks to a small server on this computer, and the server talks to the machine. The computer
has to stay on; PhotonCanvas itself can be closed.

This is an add-on: it adds **Remote access** to the Laser tab. Turning it off in Settings → Add-ons
takes the button away; a server that's already running keeps running until you stop it.

It works in any phone browser, so nothing needs installing: add the page to the home screen and it
opens like an app. On Android there's also an app, **PhotonCanvas Remote** (see
[The Android app](#the-android-app)).

> **Safety:** Starting a job from the phone fires the laser, maybe with nobody at the machine, so it's
> **off until you turn it on** (**Allow starting jobs from the phone**, under
> [Safety](#safety)). Even then, the phone makes you look at a fresh picture of the bed before it
> starts (see [Starting a job from the phone](#starting-a-job-from-the-phone)). Look at it properly.
> The machine's own flame alarm stays on, and the phone shows its alarms.

## Setting it up

<!-- windows: RemoteAccessWindow -->

Click **Remote access** on the **Laser** tab (in the **Machine** group). The window has everything
the phone needs.

### Server

- **Start server** starts the remote server. It runs without a window and keeps running when you
  close PhotonCanvas. The line above the buttons says whether it's running.
- **Stop server** stops it. Phones can't connect until it's started again.
- **Start the server when Windows starts** (Windows only) starts it whenever you log in, so the phone
  works even before you open PhotonCanvas.

The first time the server starts, Windows may ask whether *PhotonCanvas Server* may use the network.
Allow it on **private networks**, or the phone won't reach it.

The server makes its own connection to the machine, next to PhotonCanvas's (the machine takes several
at once). It finds the machine at the address PhotonCanvas last connected to, and looks for it on the
network if that stops working. It needs the [connection keys](machine.md#connection-keys), so connect
once from PhotonCanvas first.

### Connect your phone

- **Address**: this computer's address on your network (and its Tailscale address, if it has one; see
  [Away from home](#away-from-home)). The QR code and the address below it follow your choice.
- The **QR code**: point the phone's camera at it and open the link. The page opens already connected.
- **Access code**: what the phone needs to connect, 16 letters and digits. Without the QR code, open
  the address in the phone's browser and type the code. The phone remembers it.
- **New code** makes a new access code. Phones that used the old one must scan the new QR code or
  type the new code. Use it if a phone you don't trust has seen the code.

Ten wrong codes from one device lock that device out for five minutes.

### Android app

When this computer has the Android app to give out, a second QR code installs it: scan it with the
phone, open the download, and allow installing apps from the browser when Android asks (the first
time only). The server must be running. See [The Android app](#the-android-app).

### Designs the phone can load

The phone lists your recent designs in PhotonCanvas (newest first), then the designs in the folders
listed here, and in the folders inside them (two levels down). With no folders listed, your Documents
folder is searched.

- **Add folder…** adds a folder.
- **Remove** takes the selected folder off the list (the designs stay where they are).

### Safety

- **Allow starting jobs from the phone** (off at first): while it's off, the phone can load a job,
  watch, take pictures, pause and stop, but only the machine's button starts a job. Turning it on
  shows a full warning first: the laser fires with maybe nobody at the machine, the cameras are blank
  while it runs, and anyone with the access code could start a job. Click **I understand, allow it**
  only if you accept that; **Keep it off** leaves it off. Turning it off takes effect at once, even
  for a phone that's connected.

## On the phone

The page has three tabs along the bottom.

### Machine

- The machine's state, in the same words as PhotonCanvas's machine panel, and its focus distance.
- The job on the machine: its name, who sent it (*Loaded from the phone*, *Sent from the desktop
  app*, or *From xTool Studio, the machine's screen or another computer*), a progress bar, the time
  it has taken and about how long is left.
- **Pause** and **Continue**, and **Stop** (it can't be continued after that). For a job that's loaded
  but not started, **Cancel job** takes it off the machine.
- For a loaded job: **Look at the bed** and **Start job** (see
  [Starting a job from the phone](#starting-a-job-from-the-phone)).
- **Camera**: **Overview** or **Close-up**, and **Take picture**.

> **Note:** The machine blanks both cameras for the whole job, so pictures taken while a job runs come
> out black. You can't watch the job itself. When it ends, the server takes a picture of the result
> (see [History](#history)).

The page updates by itself while it's open. The phone buzzes when a job finishes or stops, and a red
banner shows the machine's alarms (a possible flame, the machine moved or tilted, other errors).

### Designs

Tap a design to see what it would do. It's built exactly as **Ship It** builds it, with your current
job settings (passes, pauses, fan) from PhotonCanvas. You see a picture of what will be marked, then
the same checks **Ship It** shows: the layers, size and time, the focus distance, and anything left
out or worth a warning. If a job is already loaded and not started, it says so: loading this one
replaces it.

**Load onto machine** sends the job. The machine then waits, exactly as after **Ship It**: nothing
moves or fires until it's started, from the phone or with the machine's button. Right after loading,
the server takes a picture of the bed and shows it, ready for **Start job**.

A design that can't be sent from PhotonCanvas can't be sent from the phone either (for example when
step-downs would lower the head too close to the material); the reason is shown in red.

### History

Every job the machine runs while the server is on, whoever started it, with a picture of the bed from
when it was loaded and another after it ended. Tap a picture to see it full size. It says whether the
job finished or was stopped, how long it took, and whether it was started from the phone. Jobs loaded
from the phone also go into PhotonCanvas's own job history.

## The Android app

**PhotonCanvas Remote** does everything the page does, with the same three tabs, and:

- **Scan the QR code** on its first screen reads the code in **Remote access** with the phone's camera
  (through Google Play services: the app itself gets no camera permission). Or type the address and
  the access code.
- **Try the demo machine** runs everything on a pretend machine: nothing real moves or fires. Handy
  for learning it, or showing someone.
- It remembers the computer. **⋯ → Change computer** forgets it; **⋯ → About and privacy** says what it
  does with your data (nothing leaves the phone except to your own computer).
- **Encrypted:** the app talks HTTPS to the computer's own certificate, which the QR code carries (the
  server makes it the first time it starts and keeps it). A typed address learns it on the first
  connection. If the computer's certificate ever changes (PhotonCanvas reinstalled, its remote settings
  reset), the app refuses it and asks you to scan the QR code again.
- **Notifications** (**⋯ → Settings**), even with the app closed:
  - **While a job is on the machine** (the standard setting): a notification with the job's progress,
    the time left and when it'll be done, and a **Pause** button (stopping is in the app, which asks
    first); a notification when it ends, with the picture of the result; the machine's alarms (a
    possible flame, the machine moved), loud; and a warning if the phone loses the computer during a
    job. It stops by itself after the job.
  - **Always**: the same, also for jobs started at the machine or in PhotonCanvas, and alarms between
    jobs. It keeps a connection open, so it uses a little more battery.
  - **Only while the app is open**: none.

  **Send a test notification** checks they arrive. Newer Android asks once whether the app may notify.
  If notifications stop while the phone sleeps, set the app's battery use to *Unrestricted* in
  Android's settings.
- **Live** on the camera card shows pictures one after another (a few a second) while nothing runs.
- **Run again** in **History** opens a design the phone loaded before (while its file is still there).
- The job card says when it'll be done (*done about 4:15 AM*); so does the phone page.

Install it from **Remote access** (the *Android app* QR code), or, once it's published, from the
Play store.

## Starting a job from the phone

This is off until you turn on **Allow starting jobs from the phone** in **Remote access** (see
[Safety](#safety)). While it's off, **Start job** says so and nothing starts: press the machine's
button instead.

When it's on, **Start job** only works on a picture of the bed that the server took **less than 2 minutes ago,
with that job loaded**. So:

1. Load the design (or use a job that's already loaded, sent from PhotonCanvas).
2. Look at the picture: **Look at the bed** takes a new one. Is the material in place and flat, is
   nothing in the way, is the lid as you want it?
3. **Start job**, and confirm.

A picture from before the job was loaded, or an older one, isn't enough: take a new one. If the
machine doesn't start, the phone says what the machine reports; press its button instead.

## Away from home

The phone reaches this computer on your home Wi-Fi. To use it from anywhere, install
Tailscale (free, from tailscale.com) on this computer and on the phone, and sign in to the same
account on both. Tailscale connects the two privately and encrypted, wherever the phone is.

- **The Android app switches by itself.** It knows all the computer's addresses (the QR code carries
  them, and the computer tells it again each time it connects, so it learns the Tailscale address the
  next time you're home) and uses whichever answers: home Wi-Fi when you're in, Tailscale when you're
  out. Keep Tailscale switched on, on the phone.
- **The phone page:** choose the Tailscale address in **Remote access** (it starts with 100.) and open
  that one away from home.

> **Warning:** Don't open a port on your router for the server. The access code is the only lock on
> it, and the laser is on the other side.

## What's different from PhotonCanvas

- **Serial numbers and spreadsheet rows** move on after each run in PhotonCanvas, in the open design.
  The phone doesn't change design files, so they don't move on after a job started from the phone. The
  check says so for such designs; open the design in PhotonCanvas for the next numbers.
- **Sections and layer by layer** aren't on the phone: it always sends the whole design.
- PhotonCanvas follows only the jobs it sent itself. When a job from the phone runs, its job bar shows
  the machine running, but it doesn't follow it on the work area or log it as the open design.

## If the phone can't connect

- Is the server running? **Remote access** says so.
- Is the phone on the same Wi-Fi as the computer (or both on Tailscale)?
- Did Windows ask about the network when the server started? If you said no, allow
  *PhotonCanvas Server* in Windows Security → Firewall & network protection → Allow an app through
  the firewall, for private networks.
- *Wrong or missing access code*: scan the QR code again, or type the code from **Remote access**.
- *Not connected to the machine*: the line under the machine's state says why, for example that the
  connection keys aren't set up, or that the machine can't be reached.

The server's log is in the `logs` folder of the [data folder](machine.md#the-data-folder)
(`server-` and the date). Programmers: the server's API is described in `docs/remote-api.md` in the
source.

## Try it

- [Control the laser from your phone](tutorial:mach-remote)
