# A relief from a 3D model
<!-- category: Pictures -->
<!-- summary: Carve a 3D model (STL, OBJ, STEP) into the surface as a relief. -->
<!-- page: relief.md -->
<!-- order: 80 -->
A 3D model seen from one side becomes a depth map: its top is the surface, its lowest point the
deepest. It goes on a depth-slices layer to be carved down step by step. Carving slices hasn't been
tried on the machine yet.

## Open 3D model
<!-- target: ribbon:Import/3D model -->
<!-- wait: window:3D model to relief -->
Click **3D model** on the **Import** tab.

## Open the model
<!-- target: button:Open STL -->
Click **Open STL…** and pick an STL, OBJ or STEP file. The depth map appears on the right.

## Choose the view
<!-- target: field:View -->
Look at it **From the top**, **From the front** or **From the side**.

## Set the size
<!-- target: field:Size (mm) -->
How big the relief is on the work area, along its longer side.

## Carve around it?
<!-- target: button:Carve around the model (it stands proud) -->
Tick it to carve away everything round the model, so it stands proud.

## Add it
<!-- target: button:Add to design -->
<!-- wait: closed:3D model to relief -->
Click **Add to design**. It goes in the middle of the work area on a new depth-slices layer.

## Preview the relief
<!-- target: ribbon:Picture/Relief preview -->
<!-- wait: window:Relief preview -->
With it selected, **Relief preview** shows it lit as the carved surface.
