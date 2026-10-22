# Step down for thick cuts
<!-- category: Layers and settings -->
<!-- summary: Lower the focus a little before each pass so it follows a deep cut or engraving down. -->
<!-- page: layer-settings.md#step-down -->
<!-- order: 150 -->
Cutting thick material takes several passes, and each one goes deeper than the focus. Step-down
lowers the head a little before each pass, so the beam stays sharp where it's cutting. It works on
Cut, Score and Engrave layers.

## Several passes
<!-- target: panel:Layer settings -->
Select the layer and set **Passes** to the number of passes the cut needs, for example 4.

## Step per pass
<!-- target: field:Step down/pass -->
Under **More settings**, **Step down/pass**: how far to lower the head before each pass after the
first, about the depth one pass cuts.

## First step
<!-- target: field:First step down -->
**First step down**: also lower it this much before the first pass (often 0).

## Check the depth
<!-- target: panel:Job -->
The job's checks say how far the head goes down in all. Make sure the material is at least that
thick. The head goes back up afterwards.
