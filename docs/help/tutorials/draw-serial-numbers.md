# Mark a serial number
<!-- category: Drawing -->
<!-- summary: Put a serial number in a text so every run marks the next one. -->
<!-- page: drawing.md#fields-serial-numbers-dates-and-spreadsheets -->
<!-- order: 85 -->
Put a serial number in a text and every run marks the next one: 0001, 0002, 0003 … The number is
saved with the design, so the count carries on next time.

## Add a text
<!-- target: ribbon:Home/Text -->
<!-- wait: shapes:+1 -->
Click **Text** on the **Home** tab, then click the work area.

## Insert the field
<!-- target: button:Insert field -->
Type your words, such as "No. ", then click **Insert field** and choose **Serial number, 4 digits**. The work area shows the number.

## Open Variables
<!-- target: ribbon:Home/Variables -->
<!-- wait: window:Variables -->
Click **Variables** (the `{x}` icon) to set where the count starts.

## Set the numbers
<!-- target: field:Next number -->
Type the number the next run marks, and the **Step** added after each run.

## Stop at the last one
<!-- target: button:Stop after -->
Tick **Stop after** and type the last number: past it the job won't send, so nothing is marked twice.

## Done
<!-- target: button:OK -->
<!-- wait: closed:Variables -->
Click **OK**. After each finished run the number moves on by itself; `Ctrl+Z` puts it back if a run went wrong.
