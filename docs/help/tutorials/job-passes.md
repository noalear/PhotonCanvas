# Passes and pauses
<!-- category: Running jobs -->
<!-- summary: Choose how passes run across the job, and add pauses to let the material cool. -->
<!-- page: running-jobs.md#passes-and-pauses -->
<!-- order: 30 -->
When layers have more than one pass, choose whether the whole job repeats round by round or each
path finishes its passes first. Pauses give the material time to cool, which matters for thin
sheet and anything that warps.

## Choose how passes run
<!-- target: text:Passes -->
**Round by round** runs every layer's first pass, then every second pass, and so on.
**Path by path** runs all of a path's passes before moving on.

## Pause between rounds
<!-- target: text:s between rounds -->
Seconds to wait before each new round, e.g. to let the material cool or to look. Only with
**Round by round**.

## Pause after each path
<!-- target: text:s after each path -->
Seconds with the laser off after every pass of each cut or score path. **Checks** says how much time
it adds.

## See the result
<!-- target: panel:Job -->
The rounds and pauses show on the timeline, and the estimate changes. Layers that spread the heat
work out their own waits, so these settings hide when every layer does.
