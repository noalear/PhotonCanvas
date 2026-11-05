# PhotonCanvas Remote: privacy policy

*Last updated: 23 October 2026*

PhotonCanvas Remote is a phone app that connects to PhotonCanvas running on your own computer, so you
can watch and control your laser.

**What the app collects:** nothing. It has no accounts, no advertising, no analytics and no crash
reporting, and it sends nothing to the developer or to anyone else.

**What it stores, and where:** the address of your computer and its access code, so it can connect
again. They are kept in the app's private storage on your phone, are not backed up to the cloud, and
are only ever sent to the computer you connected it to. **⋯ → Change computer** removes them, and so
does uninstalling the app.

**What it sends to your computer:** the requests you make (status, pictures, designs, starting and
stopping jobs) and the access code with each one. When your computer's server has its certificate
(it makes one the first time it starts), the app connects over HTTPS, encrypted and pinned to that
certificate alone. With a server that has no certificate, the app falls back to plain, unencrypted
HTTP on your home network; over Tailscale the connection is encrypted by Tailscale either way.
Pictures from the laser's cameras and your designs come from your computer to the app and are not
stored after you leave the screen.

**Notifications:** if you allow them, the app keeps a connection to your computer while a job runs
(or always, if you choose) and shows the job's progress, its end and the machine's alarms as
notifications on your phone. That connection is only between your phone and your computer.

**The camera:** scanning the QR code uses Google Play services' code scanner. It is a Google
component: it runs inside Google Play services on your phone, under Google's own terms and privacy
policy, and may communicate with Google. The app only receives the text of the code, and does not
have permission to use the camera itself.

**Children:** the app is for adults operating a laser; it is not directed at children.

**Changes:** if this policy changes, the new version will be published at this address with a new date.

**Contact:** *(the contact email shown on the app's Play store page)*
