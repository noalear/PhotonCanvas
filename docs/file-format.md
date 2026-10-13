# xTool project files: `.xs` (v2) and `.xcs` (v1)

Neither format is encrypted. `.xs` is a ZIP of pretty-printed JSON; `.xcs` is one JSON document. The default save format is v2 `.xs`. `.txs` and `.txcs` are the template variants.

Real samples to test against: `reference/exts/<MODEL>/assets/Tutorial-*.xs` (23 files). The v1 description below comes from reading code only; no `.xcs` sample has been found yet.

## Detecting the format

- The file starts with `PK` → v2.
- Otherwise parse it as JSON:
  - It is v2 if any of these hold: `__v2__: true`, `meta.version` starts with "2", `schemaMeta.schemaVersion` starts with "2", or it has a `v2Config`, `profiles` or `bindings` key.
  - It is v1 if `canvas` is an array, `device` is an object and `extId` is a string.

## v2 `.xs` ZIP layout

```
.format                                   "v2"
meta/persistence-meta.json                {"schemaVersion":"2.0.0","protocol":"xcs-workspace-v2"}
meta/compat.json                          optional
project.json
profiles.json                             optional, shared processing profiles
canvases/<canvasId>.json
canvases/<canvasId>/displays-<N>.json     display chunks (concatenate all on read)
devices/device-<urlencoded deviceId>.json processing settings
resources/<sha256>.<ext>                  bitmaps, 3D models, ... (+ <same>.meta.json {ref, metadata})
vectors/svg/index.json, data-0.json       optional: large dPaths stored out of line (vectorRef)
```

Writer details:
- Entries are deflated at level 6 (fflate), and JSON is written with `JSON.stringify(v, null, 2)`.
- Floats in displays are rounded to 6 decimal places.
- Readers accept `schemaVersion` "2.0.0" or "1.0.0".

### project.json

- Markers and schema: `__v2__: true`, `version: "2.0.0"`, `schemaMeta {schemaVersion, format: "directory", migratedFrom?}`.
- Identity: `projectId`, `projectTraceID`, `projectName`.
- Active selection: `activeCanvasId`, `activeDeviceId`.
- Type: `projectType` ("single-device" or "multi-device").
- Version info: `versionInfo {source, appVersion, savedAt, minRequiredVersion}`.
- Timestamps and modules: `created`, `modify` (epoch ms), `modules {canvases: [], devices: []}`.
- Cover: `cover` (a data-URL PNG).

### canvases/&lt;id&gt;.json

`{id, title, hidden, extendInfo{rulerPluginData, gridOptions}, layerData, groupData, chunkLayout{displayCount, chunkCount, chunkIndexes}}`

- `layerData` maps a colour key to a layer: `"#a958ff": {name, order, visible}`. **Layers are keyed by colour.**
- `groupData` maps a group id to a group: `"g-<uuid>": {groupName, groupTag, visible, enableTransform, zOrder, rotation}`.

### Displays (canvas elements)

`displays-N.json` holds `{canvasId, chunkIndex, displays: [...]}`.

Common keys:
- **Identity and grouping:** `id, name, type`, `sourceId`, `groupTag`/`groupTags[]`, `layerTag`, `layerColor`.
- **Transform:** `x, y` (top-left of the bounds), `angle` (degrees), `scale{x,y}`, `skew{x,y}`, `pivot{x,y}`, `offsetX/offsetY` (origin), `transform [a,b,c,d,tx,ty]?`.
- **Size and state:** `width, height`, `zOrder`, `visible`, `lockRatio`, `isClosePath`.
- **Paint:** `fill{paintType, visible, color(int), alpha}`, `stroke{..., width, cap, join}`, `isFill`, `lineColor`.

Units are **mm**. This is inferred from coordinate ranges, and from imported SVG paths being scaled by 0.352778 (= 25.4/72).

| type | extra fields |
|---|---|
| `PATH` | `dPath` (SVG path in local units), `points`, `fillRule`, `isCompoundPath`, `graphicX/Y`, `vectorRef?` |
| `RECT` | `radius`, `maxRadius` |
| `CIRCLE` | (none) |
| `LINE` | `endPoint{x,y}` |
| `TEXT` | `text`, `style{fontSize, fontFamily, letterSpacing, leading, align, curveX/Y, isWeld}`, `charJSONs` (one PATH per glyph), `fontData` |
| `BITMAP` | `resourcePath`, `originWidth/Height`, `dpi{dpiX,dpiY}`, `grayValue, sharpness, brightness, contrast, isGray`, `filterList(_V2)`, `mask` |
| `MODEL_3D` | `matrix` (4×4), `sizeX/Y/Z`, `modelDataRef` |

Other types defined in the engine: `REGULAR_POLYGON, STAR, PEN, COMPONENT, POLYGON, LINE_SVG, PARAMETRIC_ARRAY, STICKER_GROUP, BITMAP_3D`.

### devices/device-&lt;id&gt;.json (processing settings)

```
{ id, deviceCode, extId, extName, snCode?, power: [..],
  processing: { <canvasId>: { activeMode: "LASER_PLANE",
    modes: { <MODE>: {
      ignoredDisplayIds: [],
      data: { material, lightSourceMode, thickness, isProcessByLayer, pathPlanning, fillPlanning, scanDirection, fanGear, purifierGear, ... },
      profiles: { <pid>: { id, processingType, values: {...} } },
      patches:  { <patchId>: { id, profileId, source: "material", material: {...}, overrides: {...} } },
      bindings: [ { bindingId, baseProfileId, patchIds: [], displayIds: [], shadowOf? } ] } } } } }
```

To get a display's effective settings, start from its binding's base profile. Look the profile up in the device file first, then in `profiles.json`. Then apply each listed patch's `overrides` in order. Bindings with `shadowOf` hold the parameters for the display's other, inactive processing types.

Common `values` keys:
- **Basic:** `power` (%), `speed` (mm/s), `repeat` (passes), `density`, `dpi`, `processingLightSource`.
- **Bitmap:** `bitmapScanMode`, `bitmapEngraveMode`, `dotDuration`, `scanAngle`, `crossAngle`, `enableOverScan`, `overScanLength`.
- **Kerf:** `enableKerf`, `kerfDistance`.
- **Breakpoints:** `enableBreakPoint`, `breakPoint*`.
- **Z-drop:** `cuttingDrop`, `cuttingDropValue`.
- **Source and pulse:** `laser`, `processHead`, `frequency`, `pulseWidth`, `mopaFrequency`, `wobble*`.

## v1 `.xcs` (single JSON)

- **Maps:** JS Maps are encoded as `{"dataType":"Map","value":[[k,v],...]}`.
- **Top level:** `version, name, canvasId, canvas[], projectTraceID, created, modify, cover, minRequiredVersion, extId, extName, device{...}, meta[]`.
- **`canvas[]` entries:** `{id, title, hidden, displays[], layerData, groupData, ...}`. Displays use the same shape as in v2.
- **`device.data`:** `Map<canvasId, {mode, data:{<MODE>:{material,...}}, displays: Map<displayId, {processingType, data:{<PROCESSING_TYPE>:{parameter:{official|customize:{power,speed,repeat,...}}}}}>}>`.

## Enums

Both are defined in `exts/ext-runtime/*/xcs-ext-base.js`.

- **processingType (laser):** `VECTOR_CUTTING`, `VECTOR_ENGRAVING`, `FILL_VECTOR_ENGRAVING`, `BITMAP_ENGRAVING`, `COLOR_ENGRAVE`, `COLOR_FILL_ENGRAVE`, `RELIEF`, `INTAGLIO`, `INNER_*`.
- **modes:** `LASER_PLANE`, `LASER_FIXTURE`, `LASER_CYLINDER(_ROLLER/_CATCH)`, `FREE_PLANE`, `PASS_THROUGH`, `SUPER_LASER_PLANE`, `LASER_CONVEYOR_FEEDER`, `CURVE_PROCESS`, `BATCH_PROCESS`, `RELIEF_PROCESS`.

## Design → job pipeline (theirs)

1. **JS preparation.** `GcodeBuilder` in `xcs-ext-base.js` renders each element to SVG/PNG files and writes a `manifest.json`. Fields seen so far: `type, filename, dpi, layer_id, priority_id, merge_id, repeat, processDirection`. Power is ×10 (per-mille), speed is ×60 (mm/min), and device-specific G-code head/tail templates are included.
2. **Native path planning.** It then calls `path_planning.genGCodeSync({dir, dst, manifest})` in the native `lib_path_planning.dll`.
3. **Output.** The result is `gcode.txt`, or `gcode.bgc` when the device's `gcodeFormat` includes "bin".
4. **Upload.** The output is uploaded to the device.

## Open questions

- No real v1 sample yet.
- The `power: [..]` array on the device record is unclear.
- The full key set of the G-code `manifest.json` input is not yet known.
