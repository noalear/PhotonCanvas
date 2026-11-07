# See the run order and pictures as burned
<!-- category: The workspace -->
<!-- summary: Colour the paths by when they run, and see pictures as they'll come out on the material. -->
<!-- page: canvas.md#processing-order -->
<!-- order: 120 -->
Two views that show the job rather than the drawing: the order the laser takes, and how a picture
will really look on your material. You need a design with a few shapes; the last step also needs
a picture on a layer whose material has had a tone test.

## Show the processing order
<!-- target: ribbon:View/Processing order -->
Click **Processing order**. Each path is coloured by when it runs: blue first, red last. Dashed
lines are the moves between them.

## Check the order
<!-- target: canvas -->
Holes should come before the outlines around them. The key at the top says how many paths there
are. Click the button again to hide it.

## The same in the job bar
<!-- target: panel:Job -->
The **Order** chip in the job bar is the same switch.

## Pictures as burned
<!-- target: ribbon:View/Pictures as burned -->
Click **Pictures as burned**. A picture on a layer whose material has a tone test (Materials & Tests → Tests → **Tone test**) shows as it will come out, in the material's own colours. Other pictures look as usual.
