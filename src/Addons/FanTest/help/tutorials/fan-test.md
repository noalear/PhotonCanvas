# Test the exhaust fan's speed
<!-- category: Machine and settings -->
<!-- summary: Check that an xTool inline fan follows the speed jobs send. -->
<!-- page: fan-test.md -->
<!-- order: 95 -->
The fan test tries the ways an xTool inline fan's speed is set, one at a time. Only fan
commands are sent: the laser stays off and the head doesn't move. You need the machine connected
(**Connect** at the right end of the ribbon).

## Open the fan test
<!-- target: ribbon:Machine/Fan test -->
Click **Fan test** in the Machine tab's Settings group. It shows once **xTool inline fan** is ticked in Settings → Equipment.

## Read what the machine says
<!-- target: button:Read the fan's state -->
Click **Read the fan's state**. The note under it says whether the machine has a stored fan speed.

## Run the fan job
<!-- target: button:Load the fan job -->
Click **Load the fan job**, then press the machine's button. Listen: if the fan steps up four
times, the gear setting works; if it runs flat out all the way, it doesn't.

## Try a single command
<!-- target: button:Send now -->
Pick a gear and click **Send now**. The log shows what was sent and what the machine replied.
