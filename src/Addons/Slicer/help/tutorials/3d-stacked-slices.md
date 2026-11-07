# Slice a 3D model into stacked layers
<!-- category: 3D and slices -->
<!-- summary: Turn an STL, OBJ or STEP model into numbered layers of your material, ready to cut and glue up. -->
<!-- page: 3d-slices.md#stacked-layers -->
<!-- order: 10 -->
A 3D model from a printing site or your CAD program can be built in wood or card: cut it into flat
layers as thick as your sheet, then glue them up in order. You need a model file (STL, OBJ or STEP)
and the real thickness of your sheet.

## Open 3D to slices
<!-- target: ribbon:Import/3D to slices -->
<!-- wait: window:Stacked slices -->
Click **3D to slices** on the **Import** tab. The **Stacked slices** window opens.

## Open the model browser
<!-- target: button:Open a 3D model… -->
<!-- wait: window:Open a 3D model -->
Click **Open a 3D model…**. It shows the models in your models folder as pictures.

## Pick a model
<!-- target: button:Choose folder… -->
<!-- wait: closed:Open a 3D model -->
No models showing? Click **Choose folder…** and pick the folder with your model files. Then
double-click a model's picture to open it.

## Stand it the right way up
<!-- target: field:Stands with -->
Choose which way is up. The slices are cut across the up direction, so pick the way that stacks
best: a figure standing, a plaque lying flat.

## Set the height
<!-- target: field:Height Z (mm) -->
Type the height you want. With **Keep proportions** on, the width and depth follow. The height sets
how many slices there are.

## Type your material's thickness
<!-- target: field:Material (mm) -->
Type the thickness you measured on your sheet. There's one slice per thickness.

## Choose the outline
<!-- target: field:Outline -->
A slice is the same shape top and bottom. **Middle** splits the difference, **Outer** leaves extra
to sand back, **Inner** never sticks out.

## Check the summary
<!-- target: text:the stack stands -->
Read how many slices and parts you'll get, and any warnings. Drag the preview to turn it, and use
the mouse wheel to zoom in on a spot, middle-drag or the arrow keys to move it (double-click or **F** to see it all again); red parts start in mid-air.

## Make the slices
<!-- target: button:Make the slices -->
Click **Make the slices**. Every part goes on the work area, numbered and marked, on as many pages
as it takes.

## Close the window
<!-- target: button:Close -->
<!-- wait: closed:Stacked slices -->
Click **Close**. Set the **Slices · cut** layer for your material before you send the job.
