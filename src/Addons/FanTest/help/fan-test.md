# Exhaust fan test

This add-on adds **Fan test** to the Machine tab's **Settings** group: a window for finding out
which command, if any, changes the exhaust fan's speed, so the fan can run quieter when there
isn't much smoke.

Everything in it only touches the fan. The laser stays off and the head doesn't move. Each send
asks first, and the **Log** at the bottom lists what was sent and when, with the machine's
replies, so you can match what you heard to the command that caused it.

## Why the fan setting may do nothing

**Exhaust fan gear** (Settings → Equipment) changes the `M9064 B…` line at the start of every
job. xTool Studio writes that line only for xTool's add-on **inline duct fans** (the SafetyPro
IF2, and the older duct fan): it's how they're told their speed. xTool Studio has no speed setting
for the F2 Ultra's own exhaust fan, only the run-on time after a job (**Device settings → Exhaust
& sleep**). So the stock fan may ignore the gear and run at full speed for every job. This window
tests that, and two other ways the speed might be set.

<!-- windows: FanTestWindow -->

![The Exhaust fan test window: what the machine says, stored fan speed, one fan command now, the laser-off fan job and the log](images/window-fan-test.png)

## 1. What the machine says

**Read the fan's state** asks the machine about the exhaust fan (connected or not, plus anything
else it reports) and its stored fan settings. If the answer includes a stored fan speed
(`smokeFanSpeed`), the machine knows that setting: try 2. Only reads.

## 2. Stored fan speed (experimental)

xTool's P2S keeps its fan speed as a stored setting, 10 to 100%. The F2 Ultra may keep one too,
though xTool Studio doesn't show it. **Write to the machine** stores the **Fan speed (%)**, then
reads it back. Run a job (or the fan job, 4) and listen. Write 100 to put it back.

## 3. One fan command now

Builds one fan command from **Send to**, **Kind** and **Gear**, shows it, and **Send now** sends it
straight away:

| Send to | Command |
|---|---|
| The machine itself | `M9064 A2` (manual) or `M9064 B2` (automatic) |
| Duct fan port (P70) | `G198 P70 "M9064 A2"`: passed on to xTool's older duct fan |
| IF2 inline fan port (P78) | `G198 P78 "M9064 A2 C3"`: passed on to the IF2 (`C2` with gear 0, `C4` automatic) |

Gears are 0 (off) to 4 (fastest). Jobs send the automatic form (`B`); manual (`A`) holds the
gear. If gear 0 turns the fan off, send gear 3 or 4 to bring it back.

## 4. Laser-off fan job

**Load the fan job** uploads a job that only runs the fan: gears 1, 2, 3 and 4 in turn, each for
**Seconds per gear** (15 to begin with), sent to the machine and both duct fan ports, automatic
or manual (**Gears**). It waits for the machine's button, like any job. A job already loaded
but not started has to be cancelled first.

Listen while it runs:

- The fan steps up four times: the job's fan gear works, and **Exhaust fan gear** in
  Settings → Equipment will make every job quieter.
- Full speed from start to end: the machine ignores the gear.

After the job, the fan keeps running for the exhaust run-on time.

Not yet tried on the machine (2026-11-21).

## Tutorials

- [Test the exhaust fan's speed](tutorial:fan-test)
