# PhotonCanvas Remote for Android

The phone app: everything the phone page does (machine state, camera, pause/stop, designs, load and
start, history), as an app that scans the QR code from **Laser → Remote access** itself. It talks to
the remote server on the user's computer (`docs/remote-api.md`).

| Project | What it is |
|---|---|
| `src/PhotonCanvas.Remote` | What phone and server say to each other (shared records), the HTTP client, and the demo machine. No dependencies. |
| `src/PhotonCanvas.Remote.App` | The screens (Avalonia), shared by Android and the desktop window. |
| `src/PhotonCanvas.Remote.Android` | The Android app: activity, QR scanner, manifest, icon. Not in `PhotonCanvas.slnx` (it needs the Android tools). |
| `src/PhotonCanvas.Remote.Desktop` | The same app in a window on the computer, for trying it and for development. |

## Setting up the computer (once)

1. The .NET Android workload, in a PowerShell opened as administrator:
   ```
   dotnet workload install android
   ```
2. The Android SDK and a JDK (about 700 MB; this accepts the Android SDK licence):
   ```
   dotnet build src/PhotonCanvas.Remote.Android -t:InstallAndroidDependencies -f net10.0-android -p:AndroidSdkDirectory=%USERPROFILE%\Android\Sdk -p:JavaSdkDirectory=%USERPROFILE%\Android\jdk -p:AcceptAndroidSDKLicenses=True
   ```
   The project finds them in `%USERPROFILE%\Android` by itself. Keep them out of `AppData`: programs
   started from some launchers (the Claude app, for one) have AppData redirected into a private folder
   that other programs don't see.

## Building and trying it

- **On the computer:** `dotnet run --project src/PhotonCanvas.Remote.Desktop` (no QR scanning there;
  type the address and code, or try the demo machine).
- **For the phone:** `dotnet build src/PhotonCanvas.Remote.Android -c Debug` makes
  `src/PhotonCanvas.Remote.Android/bin/Debug/net10.0-android/org.photoncanvas.remote-Signed.apk`
  (signed with a debug key: fine for trying, not for the store). The project sets
  `EmbedAssembliesIntoApk`: without it a debug APK leaves the app's own code out (the IDE copies it to
  a phone on a cable), and installed any other way it closes the moment it opens.
- **When something goes wrong** the app shows it with **Copy details** instead of closing; if it does
  close, it keeps the reason (`crash.txt` in its own folder) and shows it on the next start.

**Installing it on a phone without a cable:** the remote server offers the newest APK at
`http://<computer>:8740/app/PhotonCanvasRemote.apk` (no access code needed; it's only the app), and
**Remote access** shows a QR code for it. A release puts the APK in the server's `app` folder.
Android asks to allow installs from the browser the first time, and Play Protect may warn about an
unknown developer for a debug build.

With a cable: enable USB debugging on the phone (Settings → About → tap Build number 7 times, then
Developer options → USB debugging), then
`%USERPROFILE%\Android\Sdk\platform-tools\adb install -r <apk>`.

The headless screenshots of every screen on the demo machine were made by driving `MainView` with
Avalonia.Headless (see `tools/HelpShots` for the pattern).

## Signing for the Play store

Play takes an Android App Bundle (`.aab`) signed with your **upload key**. Google then signs the app
for phones with its own key (Play App Signing), so a lost upload key can be replaced through Play
support, but keep it safe anyway.

1. Make the key once (keytool comes with the JDK above). It asks for passwords: choose your own and
   keep them in a password manager.
   ```
   %USERPROFILE%\Android\jdk\bin\keytool -genkeypair -v -keystore %USERPROFILE%\Android\photoncanvas-upload.jks -alias upload -keyalg RSA -keysize 4096 -validity 10000
   ```
   Back the `.jks` file up somewhere safe. It must never go into the repository (`.gitignore` refuses
   `*.jks`, `*.keystore`, `*.aab` and `*.apk`).
2. Build the bundle, giving the passwords through environment variables (never on the command line
   history or in a file in the repository):
   ```
   set PC_KEY_PASS=…
   dotnet publish src/PhotonCanvas.Remote.Android -c Release -p:AndroidKeyStore=true -p:AndroidSigningKeyStore=%USERPROFILE%\Android\photoncanvas-upload.jks -p:AndroidSigningKeyAlias=upload -p:AndroidSigningKeyPass=env:PC_KEY_PASS -p:AndroidSigningStorePass=env:PC_KEY_PASS
   ```
   The bundle is `bin/Release/net10.0-android/publish/org.photoncanvas.remote-Signed.aab`.
3. Every upload needs a higher `ApplicationVersion` (the whole number in the project file) than the
   last; `Version` is what users see.

## Publishing on Google Play

As of October 2026 (check again when you get there):

- **Developer account:** $25 once, with identity verification. A **personal** account made after
  13 November 2023 must run a **closed test with at least 12 testers, opted in for 14 days in a
  row**, before it can publish to everyone. An organisation account (needs a D-U-N-S number) skips that.
- **Target API:** new apps and updates must target Android 16 (API 36) from 31 August 2026.
  `net10.0-android` does.
- **The steps:** create the app in Play Console → fill in the store listing, privacy policy, Data
  safety, content rating, target audience and app access → upload the bundle to **Closed testing**,
  add the testers (their Google accounts, by email list or a Google Group), and send them the opt-in
  link → after 14 days with 12 opted-in testers, apply for production access on the dashboard →
  production release.

### Store listing

- **App name:** PhotonCanvas Remote
- **Short description** (80 characters at most): Watch and run your laser through PhotonCanvas on your own computer.
- **Full description:**

  > PhotonCanvas Remote is the phone companion to PhotonCanvas, free laser software for your computer.
  > With PhotonCanvas's remote server running on your computer, the app shows what your laser is doing
  > and lets you run it from anywhere in the house (or from anywhere at all, with Tailscale):
  >
  > • The machine's state, the job, its progress and the time left
  > • Pause, continue and stop
  > • Pictures from the machine's cameras
  > • Your designs: check one (the same checks as the desktop app), load it, look at the bed and start it
  > • The history of every job, with a picture before and after
  > • The machine's alarms, such as a possible flame, with a long buzz
  >
  > Connecting takes one scan of the QR code PhotonCanvas shows. The app talks only to your own
  > computer: no account, no cloud, no ads, no tracking. A job only starts from the phone after you've
  > looked at a fresh picture of the bed.
  >
  > Try it without a laser: the demo machine on the first screen does everything for pretend.
  >
  > Works with the laser PhotonCanvas supports (the xTool F2 Ultra). Not made by, affiliated with or
  > endorsed by xTool.

- **Graphics:** the 512 × 512 icon (from `ic_launcher_foreground.xml` on the dark background), a
  1024 × 500 feature graphic, and at least two phone screenshots. Screenshots can be at most twice as
  long as they are wide, so take them at 1080 × 1920 (the demo machine shows every screen).
- **Category:** Tools. **Contact email:** required (shown publicly).
- **Trademarks:** keep "xTool" out of the name and the icon; saying what it works with in the
  description, with the not-affiliated line, is fine.

### App content answers

- **Privacy policy:** required (the app has a camera feature through Play services). The text is in
  `docs/privacy-policy.md`; it has to be at a public web address (for example a GitHub Pages page of
  the public releases repository, or a Google Sites page).
- **Ads:** no.
- **App access:** "All functionality works without special access: tap *Try the demo machine* on the
  first screen. The real thing needs the user's own computer running PhotonCanvas and a laser."
  Reviewers can't reach your computer, so the demo machine is what they test.
- **Content rating:** a utility; no violence, no user content, no sharing, no purchases.
- **Target audience:** 18 and over (it runs a laser).
- **Data safety:**
  - Does the app collect or share user data? **No.** (The computer's address and access code are
    stored on the phone only and sent only to the user's own computer; nothing goes to the developer
    or third parties.)
  - Is data encrypted in transit? Yes when the server has its certificate (it makes one on its first
    start): HTTPS, pinned to that certificate. With a server without one, plain HTTP on the home
    network; over Tailscale, Tailscale encrypts it either way. Since nothing is collected, Play
    doesn't ask.
  - Can users delete their data? It's only on the phone: **⋯ → Change computer** forgets it, and
    uninstalling removes it.
- **Permissions:** Internet, vibrate, notifications, and a foreground service. QR scanning uses
  Google Play services' code scanner, which needs no camera permission. It is a Google component
  that works under Google's terms and may communicate with Google (the privacy policy says so).
- **Foreground service declaration** (Play Console → App content → Foreground service permissions):
  type *connected device*. What to write: "While a laser job runs on the user's machine (an xTool F2
  Ultra controlled by PhotonCanvas on their computer, over the network), the app keeps a connection to
  it so the user sees the job's progress, can pause it from the notification, and is alerted at once
  when the machine reports a possible flame or the job ends. The user chooses this in Settings and can
  turn it off; it stops by itself after the job." Play asks for a short video: record the ongoing
  notification during a demo job (the demo machine doesn't start the watcher, so record a real job,
  or a demo of the notification). `CHANGE_NETWORK_STATE` is declared because Android requires one
  network permission for connected-device services.

## What's next for the app

- More than one saved computer.
- Reaching the computer from anywhere without Tailscale would need a relay on the internet (a server
  both sides connect out to). Possible, but it costs money to run, and puts a path to the laser on the
  internet; Tailscale gives the same without either.
