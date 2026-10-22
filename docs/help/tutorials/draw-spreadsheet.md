# Mark names from a spreadsheet
<!-- category: Drawing -->
<!-- summary: Load a CSV spreadsheet so each run marks the next row's names or numbers. -->
<!-- page: drawing.md#the-variables-window -->
<!-- order: 87 -->
Load a spreadsheet saved as CSV and each run marks the next row: names on place cards, addresses on
tags, numbers on parts. Its headings become fields such as `{Name}`.

## Open Variables
<!-- target: ribbon:Home/Variables -->
<!-- wait: window:Variables -->
Click **Variables** (the `{x}` icon) on the **Home** tab.

## Load the spreadsheet
<!-- target: button:Load CSV -->
Click **Load CSV…** and pick the file. Its first row must be the headings.

## Pick the first row
<!-- target: field:Row to mark next -->
Row 1 is the first row under the headings. That row's values are listed below it.

## Close the window
<!-- target: button:OK -->
<!-- wait: closed:Variables -->
Click **OK**.

## Put a column in a text
<!-- target: panel:Selection -->
Select a text (or add one with **Text**). Click **Insert field** under its words and choose **Spreadsheet:** and a heading.

## Run it
<!-- target: panel:Job -->
Each finished run moves on to the next row. When every row is done, the job won't send until you pick a row again.
