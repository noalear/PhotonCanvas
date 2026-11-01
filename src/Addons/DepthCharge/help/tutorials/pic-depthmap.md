# A relief from a depth map
<!-- category: Pictures -->
<!-- summary: Turn a photo into a depth map with DepthCharge and carve it in slices. -->
<!-- page: depthcharge.md -->
<!-- order: 70 -->
A depth map is a grey picture of heights: white is the surface, black the deepest. DepthCharge, a
separate program you install, makes one from a photo, and PhotonCanvas carves it in slices. Carving
slices hasn't been tried on the machine yet.

## Select a picture
<!-- target: canvas -->
<!-- wait: picture-selected -->
Click a photo on the work area. With nothing selected, **Depthmap** asks for a picture file instead.

## Open Depthmap
<!-- target: ribbon:Picture/Depthmap -->
<!-- wait: window:Depthmap with DepthCharge -->
Click **Depthmap**. The top card says whether DepthCharge was found.

## Find DepthCharge
<!-- target: button:Choose DepthCharge folder -->
If it wasn't found, click **Choose DepthCharge folder…** and pick the folder you set it up in.

## Large pictures
<!-- target: field:Large pictures -->
Leave **Automatic tiles**: a picture bigger than the model sees at once, or a wide one, is looked
at in overlapping tiles, so its depth keeps the picture's detail. **Fine tiles** is sharper still
and slower.

## Generate
<!-- target: button:Generate depthmap -->
Choose a **Model** and click **Generate depthmap**. The first run takes longer. A tiled picture
counts its tiles, then says how big its depth is.

## Shape the relief
<!-- target: field:Midtone lift -->
These sliders reshape the map without running the model again.

## Add it
<!-- target: button:Add depthmap to design -->
<!-- wait: closed:Depthmap with DepthCharge -->
Click **Add depthmap to design**. It goes over the photo on a new depth-slices layer.

## Preview the relief
<!-- target: ribbon:Picture/Relief preview -->
<!-- wait: window:Relief preview -->
With the depth map selected, **Relief preview** shows it lit as the carved surface.
