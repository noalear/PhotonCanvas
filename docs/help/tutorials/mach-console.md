# Send G-code from the console
<!-- category: Machine and settings -->
<!-- summary: Send commands straight to the machine and keep them as macros (advanced). -->
<!-- page: machine.md#the-g-code-console -->
<!-- order: 110 -->
The G-code console sends commands straight to the machine, outside any job, and keeps snippets as
macros. It's for advanced use: commands run at once and can move the head and fire the laser. You
need the machine connected (**Connect** at the right end of the ribbon). Not yet tried on the machine.

## Open the console
<!-- target: ribbon:Laser/Console -->
<!-- wait: window:G-code console -->
Click **Console** on the **Laser** tab. Not there? Tick **G-code console** in
**Settings → Equipment**.

## Send a command
<!-- target: button:Send -->
Type a command in the box at the bottom, such as `M114`, and press `Enter` or **Send**. The first
time, confirm with **I understand, send**.

## Read the answer
<!-- target: window:G-code console -->
Each command shows with the machine's answer under it. `↑` and `↓` bring back what you sent.

## Keep a macro
<!-- target: button:New macro -->
Click **New macro…**, name it and type its lines. Click it in the list to run them all.
