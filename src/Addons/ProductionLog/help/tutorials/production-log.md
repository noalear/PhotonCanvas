# Keep a production log of serial numbers
<!-- category: Running jobs -->
<!-- summary: See what each job marked, find a serial number, and carry on serial marking after a stop or a crash. -->
<!-- page: production-log.md -->
<!-- order: 96 -->
The production log writes down every job sent to the machine: when, how it ended, and the serial
numbers, spreadsheet rows and text it marked. You need nothing set up: send jobs as usual, then
open the log to look something up or to carry on where marking stopped.

## Open the log
<!-- target: ribbon:Machine/Production log -->
<!-- wait: window:Production log -->
Click **Production log** on the **Machine** tab.

## Find a part
<!-- target: field:Search -->
Type a serial number, or any text that was marked, to find the run that marked it. Click a run to
see each item, its settings and how long it ran.

## Something came out wrong
<!-- target: text:Each item this run marked -->
Tick **Spoiled** for an item that came out wrong, and **Mark again** to set the design back to it
for the next run. (This needs a run of a design with a serial number in the log.)

## Carry on
<!-- target: button:Carry on -->
After a stop or a crash, **Carry on** sets the open design's next serial number and row to just
after its last finished run. The line beside it says which run that is.

## Keep a record
<!-- target: button:Export… -->
**Export…** saves the log as a spreadsheet, one line per item marked.
