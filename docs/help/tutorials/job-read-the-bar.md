# Read the job bar
<!-- category: Running jobs -->
<!-- summary: What the timeline, the estimate and the Checks chip tell you before you ship. -->
<!-- page: running-jobs.md#the-job-bar -->
<!-- order: 15 -->
The job bar along the bottom always shows the job as it would run now, rebuilt as you edit.
Reading it before you ship catches mistakes: a layer left out, the wrong order, a job far longer
than you thought. Have something in the design.

## The timeline
<!-- target: panel:Job -->
Each block is a layer, in the order it runs, as long as it takes. Hover over one for its settings
and time; click one to bring up that layer's settings.

## The summary
<!-- target: panel:Job -->
The line after **JOB** counts the layers, paths and rounds, and gives the job's size and where its
top left corner is on the bed.

## The estimate
<!-- target: panel:Job -->
The big time on the right is how long the job takes, pauses included. It leaves out acceleration,
so real jobs run a little longer.

## Checks
<!-- target: panel:Job -->
The chip after the timeline says **All clear**, how many checks there are, or **Can't build**.
Click it for the list, with what to do about each one.

## Layer times
<!-- target: panel:Layers -->
Each layer's time also shows on its row in the **Layers** panel.
