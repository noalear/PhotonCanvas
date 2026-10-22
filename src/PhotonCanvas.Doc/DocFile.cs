using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;

namespace PhotonCanvas.Doc;

/// <summary>
/// The <c>.pcanvas</c> project file (older <c>.ytool</c> files open too): readable JSON, one entry per layer and
/// shape, geometry as SVG path data in the shape's local space.
/// </summary>
public static class DocFile
{
    public const string Extension = ".pcanvas";
    /// <summary>Extension used before the rename to PhotonCanvas; still opened.</summary>
    public const string LegacyExtension = ".ytool";
    const int Version = 1;

    public static string Serialize(DocState s)
    {
        var root = Body(s);
        if (s.OtherPages.Count > 0 || s.PageName != "Page 1")
        {
            root["pageName"] = s.PageName;
            root["pageIndex"] = s.PageIndex;
            root["pages"] = new JsonArray([.. s.OtherPages.Select(p =>
            {
                var b = Body(new DocState(p.Layers, p.Shapes) { Guides = p.Guides });
                b["name"] = p.Name;
                return (JsonNode)b;
            })]);
        }
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>One page: its layers, shapes and guides (and the project's fields).</summary>
    static JsonObject Body(DocState s)
    {
        var layers = new JsonArray();
        foreach (var l in s.Layers)
            layers.Add(new JsonObject
            {
                ["id"] = l.Id.ToString(),
                ["name"] = l.Name,
                ["parent"] = l.ParentId?.ToString(),
                ["color"] = $"#{l.Color:x6}",
                ["visible"] = l.Visible,
                ["locked"] = l.Locked,
                ["output"] = l.Output,
                ["mode"] = l.Mode.ToString().ToLowerInvariant(),
                ["laser"] = new JsonObject
                {
                    ["light"] = l.Laser.Light.Wire(),
                    ["power"] = l.Laser.Power,
                    ["speed"] = l.Laser.Speed,
                    ["passes"] = l.Laser.Passes,
                    ["pulseWidthNs"] = l.Laser.PulseWidthNs,
                    ["frequencyKhz"] = l.Laser.FrequencyKhz,
                },
                ["linesPerCm"] = l.LinesPerCm,
                ["dpi"] = l.Dpi,
                ["dotUs"] = l.DotUs,
                ["dither"] = l.Dither.ToString(),
                ["imageMode"] = l.ImageMode.ToString(),
                ["grayMinPower"] = l.GrayMinPower,
                ["kerfMm"] = l.KerfMm,
                ["tabCount"] = l.TabCount,
                ["tabWidthMm"] = l.TabWidthMm,
                ["overcutMm"] = l.OvercutMm,
                ["leadInMm"] = l.LeadInMm,
                ["fillAngleDeg"] = l.FillAngleDeg,
                ["crosshatch"] = l.Crosshatch,
                ["overscanMm"] = l.OverscanMm,
                ["stepDownMm"] = l.StepDownMm,
                ["stepDownFirstMm"] = l.StepDownFirstMm,
                ["wobble"] = l.Wobble,
                ["wobbleDiameterMm"] = l.WobbleDiameterMm,
                ["wobbleSpacingMm"] = l.WobbleSpacingMm,
                ["fillStyle"] = l.FillStyle.ToString(),
                ["halftoneCpi"] = l.HalftoneCpi,
                ["bidirectional"] = l.Bidirectional,
                ["crossAngleDeg"] = l.CrossAngleDeg,
                ["lineDwellMs"] = l.LineDwellMs,
                ["spreadLines"] = l.SpreadLines,
                ["spreadScatter"] = l.SpreadScatter,
                ["materialTone"] = l.MaterialTone is { } mt ? JsonSerializer.SerializeToNode(mt) : null,
                ["outlineAfter"] = l.OutlineAfter,
                ["scanReverse"] = l.ScanReverse,
                ["tabPowerPct"] = l.TabPowerPct,
                ["order"] = l.Order.ToString(),
                ["heatSheet"] = l.HeatSheet,
                ["heatThicknessMm"] = l.HeatThicknessMm,
                ["heatLimitC"] = l.HeatLimitC,
                ["heatPieceMm"] = l.HeatPieceMm,
                ["heatFine"] = l.HeatFine,
                ["startNearest"] = l.StartNearest,
                ["removeOverlaps"] = l.RemoveOverlaps,
                ["leadOutMm"] = l.LeadOutMm,
                ["fillTogether"] = l.FillTogether,
                ["perforateMm"] = l.PerforateMm,
                ["perforateGapMm"] = l.PerforateGapMm,
                ["dotModeMm"] = l.DotModeMm,
                ["rampMm"] = l.RampMm,
                ["passThrough"] = l.PassThrough,
                ["halftoneAngleDeg"] = l.HalftoneAngleDeg,
                ["dotWidthMm"] = l.DotWidthMm,
                ["angleStepDeg"] = l.AngleStepDeg,
                ["turnEachRun"] = l.TurnEachRun,
                ["runCount"] = l.RunCount,
                ["focusOffsetMm"] = l.FocusOffsetMm,
                ["slices"] = new JsonObject
                {
                    ["count"] = l.Slices.Count,
                    ["negative"] = l.Slices.Negative,
                    ["dropMm"] = l.Slices.DropMm,
                    ["dropEvery"] = l.Slices.DropEvery,
                    ["cleanupEvery"] = l.Slices.CleanupEvery,
                    ["cleanupPower"] = l.Slices.CleanupLaser.Power,
                    ["cleanupSpeed"] = l.Slices.CleanupLaser.Speed,
                    ["cleanupPulseWidthNs"] = l.Slices.CleanupLaser.PulseWidthNs,
                    ["cleanupFrequencyKhz"] = l.Slices.CleanupLaser.FrequencyKhz,
                    ["cleanupLinesPerCm"] = l.Slices.CleanupLinesPerCm,
                    ["cleanupWhole"] = l.Slices.CleanupWhole,
                    ["deepestFirst"] = l.Slices.DeepestFirst,
                },
                ["material"] = l.Material is { } mat ? new JsonObject
                {
                    ["id"] = mat.MaterialId.ToString(),
                    ["label"] = mat.Label,
                    ["recipeId"] = mat.RecipeId?.ToString(),
                    ["recipeTitle"] = mat.RecipeTitle,
                } : null,
            });
        var shapes = new JsonArray();
        foreach (var sh in s.Shapes)
        {
            var t = sh.Transform;
            var o = new JsonObject
            {
                ["id"] = sh.Id.ToString(),
                ["name"] = sh.Name,
                ["layer"] = sh.LayerId.ToString(),
                ["transform"] = new JsonArray(t.A, t.B, t.C, t.D, t.E, t.F),
            };
            if (sh.GroupId is { } gid) o["group"] = gid.ToString();
            if (sh.Locked) o["locked"] = true;
            if (sh.VarOffset > 0) o["varOffset"] = sh.VarOffset;
            if (sh is PathShape { ManualTabs.Count: > 0 } mt) o["tabs"] = new JsonArray([.. mt.ManualTabs.Select(t => (JsonNode)new JsonArray(t.X, t.Y))]);
            if (sh is PathShape { StartPoint: { } sp }) o["start"] = new JsonArray(sp.X, sp.Y);
            switch (sh)
            {
                case TextShape tx:
                    o["type"] = "text";
                    o["text"] = tx.Text;
                    o["style"] = new JsonObject
                    {
                        ["font"] = tx.Style.FontFamily,
                        ["sizeMm"] = tx.Style.SizeMm,
                        ["bold"] = tx.Style.Bold,
                        ["italic"] = tx.Style.Italic,
                        ["letterSpacingMm"] = tx.Style.LetterSpacingMm,
                        ["lineSpacing"] = tx.Style.LineSpacing,
                        ["align"] = tx.Style.Align.ToString(),
                        ["bendDeg"] = tx.Style.BendDeg,
                        ["pathShiftMm"] = tx.Style.PathShiftMm,
                        ["weld"] = tx.Style.Weld,
                        ["singleLine"] = tx.Style.SingleLine,
                        ["fitWidthMm"] = tx.Style.FitWidthMm,
                        ["fitHeightMm"] = tx.Style.FitHeightMm,
                    };
                    if (tx.Guide is { } guide) o["guide"] = guide.ToSvgPathData();
                    // The outline too, so the file opens identically even without the font installed.
                    o["d"] = tx.Geometry.ToSvgPathData();
                    break;
                case ParamShape ps:
                    o["type"] = "param";
                    o["kind"] = ps.Kind.ToString();
                    o["w"] = ps.WidthMm;
                    o["h"] = ps.HeightMm;
                    o["corner"] = ps.CornerMm;
                    o["sides"] = ps.Sides;
                    o["inner"] = ps.Inner;
                    break;
                case CodeShape cs:
                    o["type"] = "code";
                    o["kind"] = cs.Spec.Kind.ToString();
                    o["content"] = cs.Spec.Content;
                    o["moduleMm"] = cs.Spec.ModuleMm;
                    o["barHeightMm"] = cs.Spec.BarHeightMm;
                    o["invert"] = cs.Spec.Invert;
                    o["quietModules"] = cs.Spec.QuietModules;
                    o["ecc"] = cs.Spec.Ecc.ToString();
                    break;
                case PathShape p:
                    o["type"] = "path";
                    o["d"] = p.Geometry.ToSvgPathData();
                    break;
                case ImageShape im:
                    o["type"] = "image";
                    o["width"] = im.SizeMm.X;
                    o["height"] = im.SizeMm.Y;
                    o["tone"] = new JsonObject
                    {
                        ["brightness"] = im.Tone.Brightness,
                        ["contrast"] = im.Tone.Contrast,
                        ["gamma"] = im.Tone.Gamma,
                        ["invert"] = im.Tone.Invert,
                        ["sharpen"] = im.Tone.Sharpen,
                        ["sharpenRadiusMm"] = im.Tone.SharpenRadiusMm,
                    };
                    if (im.Clip is { } clip) o["clip"] = clip.ToSvgPathData();
                    // The original file, so nothing is lost by saving.
                    o["data"] = Convert.ToBase64String(im.Source.Encoded);
                    break;
                default:
                    continue;
            }
            shapes.Add(o);
        }
        var root = new JsonObject
        {
            ["format"] = "photoncanvas",
            ["version"] = Version,
            ["machine"] = MachineProfile.F2Ultra.DeviceCode,
            ["layers"] = layers,
            ["shapes"] = shapes,
            ["guides"] = new JsonArray([.. s.Guides.Select(g => (JsonNode)new JsonObject { ["vertical"] = g.Vertical, ["at"] = g.At })]),
            ["curve"] = CurveJson(s.Curve),
            ["variables"] = new JsonObject
            {
                ["serial"] = s.Vars.Serial,
                ["serialStep"] = s.Vars.SerialStep,
                ["serialEnd"] = s.Vars.SerialEnd,
                ["advanceAfterRun"] = s.Vars.AdvanceAfterRun,
                ["csvName"] = s.Vars.CsvName,
                ["csvText"] = s.Vars.CsvText,
                ["csvRow"] = s.Vars.CsvRow,
            },
        };
        return root;
    }

    /// <exception cref="FormatException">The file is not a readable PhotonCanvas project.</exception>
    /// <summary>A tone curve written as [[input, lightness], …], or null.</summary>
    public static Core.Imaging.ToneCurve? ReadTone(JsonNode? node)
    {
        if (node is null) return null;
        try { return node.Deserialize<Core.Imaging.ToneCurve>(); }
        catch (JsonException) { return null; }
    }

    public static DocState Deserialize(string json)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(json); }
        catch (JsonException e) { throw new FormatException($"The project file is damaged ({e.Message})."); }
        if (root?["format"]?.GetValue<string>() is not ("photoncanvas" or "ytool")) throw new FormatException("This is not a PhotonCanvas project file.");
        int version = root["version"]?.GetValue<int>() ?? 0;
        if (version > Version) throw new FormatException($"This project was saved by a newer version of {AppIdentity.ProductName}.");
        var main = FromRoot(root);
        if (root["pages"] is not JsonArray pages) return main;
        var others = pages.OfType<JsonNode>().Select(n =>
        {
            var p = FromRoot(n);
            return new DesignPage(n["name"]?.GetValue<string>() ?? "Page", p.Layers, p.Shapes, p.Guides);
        }).ToImmutableList();
        return main with
        {
            PageName = root["pageName"]?.GetValue<string>() ?? "Page 1",
            PageIndex = Math.Clamp(root["pageIndex"]?.GetValue<int>() ?? 0, 0, others.Count),
            OtherPages = others,
        };
    }

    static DocState FromRoot(JsonNode root)
    {
        try
        {
            var layers = ImmutableList.CreateBuilder<Layer>();
            foreach (var n in root["layers"]?.AsArray() ?? [])
            {
                if (n is null) continue;
                var lz = n["laser"];
                var laser = new LaserSettings
                {
                    Light = lz?["light"]?.GetValue<string>() == "red" ? LightSource.Red : LightSource.Blue,
                    Power = lz?["power"]?.GetValue<double>() ?? 0,
                    Speed = lz?["speed"]?.GetValue<double>() ?? 100,
                    Passes = lz?["passes"]?.GetValue<int>() ?? 1,
                    PulseWidthNs = lz?["pulseWidthNs"]?.GetValue<int>() ?? 200,
                    FrequencyKhz = lz?["frequencyKhz"]?.GetValue<int>() ?? 65,
                };
                layers.Add(new Layer
                {
                    Id = Guid.Parse(n["id"]!.GetValue<string>()),
                    Name = n["name"]?.GetValue<string>() ?? "Layer",
                    ParentId = Guid.TryParse(n["parent"]?.GetValue<string>(), out var pid) ? pid : null,
                    Color = SvgImport.ParseColor(n["color"]?.GetValue<string>() ?? "") ?? 0x3B82F6,
                    Visible = n["visible"]?.GetValue<bool>() ?? true,
                    Locked = n["locked"]?.GetValue<bool>() ?? false,
                    Output = n["output"]?.GetValue<bool>() ?? true,
                    Mode = Enum.TryParse<LayerMode>(n["mode"]?.GetValue<string>(), true, out var m) ? m : LayerMode.Cut,
                    Laser = laser,
                    LinesPerCm = n["linesPerCm"]?.GetValue<double>() ?? 100,
                    Dpi = n["dpi"]?.GetValue<double>() ?? 180,
                    DotUs = n["dotUs"]?.GetValue<int>() ?? 200,
                    Dither = Enum.TryParse<DitherMethod>(n["dither"]?.GetValue<string>(), true, out var dm) ? dm : DitherMethod.Jarvis,
                    ImageMode = Enum.TryParse<ImageEngraveMode>(n["imageMode"]?.GetValue<string>(), true, out var im) ? im : ImageEngraveMode.Dither,
                    GrayMinPower = n["grayMinPower"]?.GetValue<double>() ?? 10,
                    KerfMm = n["kerfMm"]?.GetValue<double>() ?? 0,
                    TabCount = n["tabCount"]?.GetValue<int>() ?? 0,
                    TabWidthMm = n["tabWidthMm"]?.GetValue<double>() ?? 0.5,
                    OvercutMm = n["overcutMm"]?.GetValue<double>() ?? 0,
                    LeadInMm = n["leadInMm"]?.GetValue<double>() ?? 0,
                    FillAngleDeg = n["fillAngleDeg"]?.GetValue<double>() ?? 0,
                    Crosshatch = n["crosshatch"]?.GetValue<bool>() ?? false,
                    OverscanMm = n["overscanMm"]?.GetValue<double>() ?? 0,
                    StepDownMm = n["stepDownMm"]?.GetValue<double>() ?? 0,
                    StepDownFirstMm = n["stepDownFirstMm"]?.GetValue<double>() ?? 0,
                    Wobble = n["wobble"]?.GetValue<bool>() ?? false,
                    WobbleDiameterMm = n["wobbleDiameterMm"]?.GetValue<double>() ?? 0.05,
                    WobbleSpacingMm = n["wobbleSpacingMm"]?.GetValue<double>() ?? 0.015,
                    FillStyle = Enum.TryParse<FillStyle>(n["fillStyle"]?.GetValue<string>(), true, out var fs) ? fs : FillStyle.Lines,
                    AngleStepDeg = n["angleStepDeg"]?.GetValue<double>() ?? 0,
                    HalftoneCpi = n["halftoneCpi"]?.GetValue<double>() ?? 30,
                    Bidirectional = n["bidirectional"]?.GetValue<bool>() ?? true,
                    CrossAngleDeg = n["crossAngleDeg"]?.GetValue<double>() ?? 90,
                    LineDwellMs = n["lineDwellMs"]?.GetValue<int>() ?? 0,
                    SpreadLines = n["spreadLines"]?.GetValue<bool>() ?? false,
                    SpreadScatter = n["spreadScatter"]?.GetValue<bool>() ?? false,
                    MaterialTone = ReadTone(n["materialTone"]),
                    OutlineAfter = n["outlineAfter"]?.GetValue<bool>() ?? false,
                    ScanReverse = n["scanReverse"]?.GetValue<bool>() ?? false,
                    TabPowerPct = n["tabPowerPct"]?.GetValue<double>() ?? 0,
                    Order = Enum.TryParse<PathOrder>(n["order"]?.GetValue<string>(), true, out var po) ? po : PathOrder.Optimised,
                    HeatSheet = n["heatSheet"]?.GetValue<string>() ?? "Aluminium",
                    HeatThicknessMm = n["heatThicknessMm"]?.GetValue<double>() ?? 0.2,
                    HeatLimitC = n["heatLimitC"]?.GetValue<double>() ?? 30,
                    HeatPieceMm = n["heatPieceMm"]?.GetValue<double>() ?? 15,
                    HeatFine = n["heatFine"]?.GetValue<bool>() ?? false,
                    StartNearest = n["startNearest"]?.GetValue<bool>() ?? false,
                    RemoveOverlaps = n["removeOverlaps"]?.GetValue<bool>() ?? false,
                    LeadOutMm = n["leadOutMm"]?.GetValue<double>() ?? 0,
                    FillTogether = n["fillTogether"]?.GetValue<bool>() ?? false,
                    PerforateMm = n["perforateMm"]?.GetValue<double>() ?? 0,
                    PerforateGapMm = n["perforateGapMm"]?.GetValue<double>() ?? 1,
                    DotModeMm = n["dotModeMm"]?.GetValue<double>() ?? 0,
                    RampMm = n["rampMm"]?.GetValue<double>() ?? 0,
                    PassThrough = n["passThrough"]?.GetValue<bool>() ?? false,
                    HalftoneAngleDeg = n["halftoneAngleDeg"]?.GetValue<double>() ?? 45,
                    DotWidthMm = n["dotWidthMm"]?.GetValue<double>() ?? 0,
                    TurnEachRun = n["turnEachRun"]?.GetValue<bool>() ?? false,
                    RunCount = n["runCount"]?.GetValue<int>() ?? 0,
                    FocusOffsetMm = n["focusOffsetMm"]?.GetValue<double>() ?? 0,
                    Slices = n["slices"] is JsonObject so ? new DepthSlices
                    {
                        Count = so["count"]?.GetValue<int>() ?? 32,
                        Negative = so["negative"]?.GetValue<bool>() ?? false,
                        DropMm = so["dropMm"]?.GetValue<double>() ?? 0,
                        DropEvery = so["dropEvery"]?.GetValue<int>() ?? 8,
                        CleanupEvery = so["cleanupEvery"]?.GetValue<int>() ?? 0,
                        CleanupLaser = new DepthSlices().CleanupLaser with
                        {
                            Power = so["cleanupPower"]?.GetValue<double>() ?? 20,
                            Speed = so["cleanupSpeed"]?.GetValue<double>() ?? 2000,
                            PulseWidthNs = so["cleanupPulseWidthNs"]?.GetValue<int>() ?? 200,
                            FrequencyKhz = so["cleanupFrequencyKhz"]?.GetValue<int>() ?? 300,
                        },
                        CleanupLinesPerCm = so["cleanupLinesPerCm"]?.GetValue<double>() ?? 300,
                        CleanupWhole = so["cleanupWhole"]?.GetValue<bool>() ?? true,
                        DeepestFirst = so["deepestFirst"]?.GetValue<bool>() ?? false,
                    } : new DepthSlices(),
                    Material = n["material"] is JsonObject mo && Guid.TryParse(mo["id"]?.GetValue<string>(), out var mid)
                        ? new Materials.MaterialRef(mid, mo["label"]?.GetValue<string>() ?? "",
                            Guid.TryParse(mo["recipeId"]?.GetValue<string>(), out var rid) ? rid : null, mo["recipeTitle"]?.GetValue<string>())
                        : null,
                });
            }
            if (layers.Count == 0) layers.Add(Layer.Default(0));
            var layerIds = layers.Select(l => l.Id).ToHashSet();

            var shapes = ImmutableList.CreateBuilder<Shape>();
            foreach (var n in root["shapes"]?.AsArray() ?? [])
            {
                var type = n?["type"]?.GetValue<string>();
                if (n is null || type is not ("path" or "image" or "text" or "code" or "param")) continue;
                var t = n["transform"]?.AsArray().Select(v => v!.GetValue<double>()).ToArray();
                var layer = Guid.TryParse(n["layer"]?.GetValue<string>(), out var lid) && layerIds.Contains(lid) ? lid : layers[0].Id;
                var id = Guid.TryParse(n["id"]?.GetValue<string>(), out var gid) ? gid : Guid.NewGuid();
                var name = n["name"]?.GetValue<string>() ?? "";
                var transform = t is { Length: 6 } ? new Affine(t[0], t[1], t[2], t[3], t[4], t[5]) : Affine.Identity;
                Guid? group = Guid.TryParse(n["group"]?.GetValue<string>(), out var grp) ? grp : null;
                if (type == "text")
                {
                    var st = n["style"];
                    var style = new TextStyle(
                        st?["font"]?.GetValue<string>() ?? "Arial",
                        st?["sizeMm"]?.GetValue<double>() ?? 10,
                        st?["bold"]?.GetValue<bool>() ?? false,
                        st?["italic"]?.GetValue<bool>() ?? false,
                        st?["letterSpacingMm"]?.GetValue<double>() ?? 0,
                        st?["lineSpacing"]?.GetValue<double>() ?? 1.2,
                        Enum.TryParse<TextAlign>(st?["align"]?.GetValue<string>(), true, out var al) ? al : TextAlign.Left,
                        st?["bendDeg"]?.GetValue<double>() ?? 0,
                        st?["pathShiftMm"]?.GetValue<double>() ?? 0,
                        st?["weld"]?.GetValue<bool>() ?? false,
                        st?["singleLine"]?.GetValue<bool>() ?? false,
                        st?["fitWidthMm"]?.GetValue<double>() ?? 0,
                        st?["fitHeightMm"]?.GetValue<double>() ?? 0);
                    var text = n["text"]?.GetValue<string>() ?? "";
                    var saved = PathGeometry.FromSvgPathData(n["d"]?.GetValue<string>() ?? "");
                    shapes.Add(new TextShape
                    {
                        Id = id, Name = name, LayerId = layer, Transform = transform, GroupId = group,
                        Text = text, Style = style,
                        Guide = n["guide"]?.GetValue<string>() is { Length: > 0 } gd ? PathGeometry.FromSvgPathData(gd) : null,
                        // Use the saved outline so the design is unchanged even if the font is missing on this PC.
                        Geometry = saved.IsEmpty ? TextLayout.Build(text, style) : saved,
                    });
                    continue;
                }
                if (type == "param")
                {
                    shapes.Add(ParamShape.Create(
                        Enum.TryParse<ParamKind>(n["kind"]?.GetValue<string>(), true, out var pk) ? pk : ParamKind.Rectangle,
                        n["w"]?.GetValue<double>() ?? 10, n["h"]?.GetValue<double>() ?? 10, n["corner"]?.GetValue<double>() ?? 0,
                        n["sides"]?.GetValue<int>() ?? 6, n["inner"]?.GetValue<double>() ?? 0.5, layer, transform, id, name, group));
                    continue;
                }
                if (type == "code")
                {
                    var spec = new CodeSpec(
                        Enum.TryParse<CodeKind>(n["kind"]?.GetValue<string>(), true, out var ck) ? ck : CodeKind.QR,
                        n["content"]?.GetValue<string>() ?? "",
                        n["moduleMm"]?.GetValue<double>() ?? 0.5,
                        n["barHeightMm"]?.GetValue<double>() ?? 12,
                        n["invert"]?.GetValue<bool>() ?? false,
                        n["quietModules"]?.GetValue<int>() ?? 0,
                        Enum.TryParse<CodeEcc>(n["ecc"]?.GetValue<string>(), true, out var ce) ? ce : CodeEcc.M);
                    shapes.Add(CodeShape.Create(spec, null, layer, transform, id, name, group));
                    continue;
                }
                if (type == "path")
                {
                    shapes.Add(new PathShape
                    {
                        Id = id, Name = name, LayerId = layer, Transform = transform, GroupId = group,
                        Geometry = PathGeometry.FromSvgPathData(n["d"]?.GetValue<string>() ?? ""),
                    });
                    continue;
                }
                var tone = n["tone"];
                shapes.Add(new ImageShape
                {
                    Id = id, Name = name, LayerId = layer, Transform = transform, GroupId = group,
                    Source = ImageSource.FromEncoded(Convert.FromBase64String(n["data"]!.GetValue<string>())),
                    SizeMm = new Vec2(n["width"]!.GetValue<double>(), n["height"]!.GetValue<double>()),
                    Tone = new ToneAdjust(
                        tone?["brightness"]?.GetValue<double>() ?? 0,
                        tone?["contrast"]?.GetValue<double>() ?? 0,
                        tone?["gamma"]?.GetValue<double>() ?? 1,
                        tone?["invert"]?.GetValue<bool>() ?? false,
                        tone?["sharpen"]?.GetValue<double>() ?? 0,
                        tone?["sharpenRadiusMm"]?.GetValue<double>() ?? 0.3),
                    Clip = n["clip"]?.GetValue<string>() is { Length: > 0 } cd ? PathGeometry.FromSvgPathData(cd) : null,
                });
            }
            var vz = root["variables"];
            var vars = vz is null ? new VariableData() : new VariableData
            {
                Serial = vz["serial"]?.GetValue<long>() ?? 1,
                SerialStep = vz["serialStep"]?.GetValue<long>() ?? 1,
                SerialEnd = vz["serialEnd"]?.GetValue<long?>(),
                AdvanceAfterRun = vz["advanceAfterRun"]?.GetValue<bool>() ?? true,
                CsvName = vz["csvName"]?.GetValue<string>() ?? "",
                CsvText = vz["csvText"]?.GetValue<string>() ?? "",
                CsvRow = vz["csvRow"]?.GetValue<int>() ?? 0,
            };
            // Locked shapes (saved per shape, set after building each).
            var locked = (root["shapes"]?.AsArray() ?? []).Where(x => x?["locked"]?.GetValue<bool>() == true)
                .Select(x => Guid.TryParse(x!["id"]?.GetValue<string>(), out var g) ? g : Guid.Empty).ToHashSet();
            var all = shapes.Select(sh => locked.Contains(sh.Id) ? sh.WithLocked(true) : sh).ToImmutableList();
            // Copies numbered ahead (each marks a different item).
            var ahead = (root["shapes"]?.AsArray() ?? []).OfType<JsonNode>().Where(x => x["varOffset"] is not null)
                .ToDictionary(x => Guid.TryParse(x["id"]?.GetValue<string>(), out var g) ? g : Guid.Empty, x => x["varOffset"]!.GetValue<int>());
            if (ahead.Count > 0) all = [.. all.Select(sh => ahead.TryGetValue(sh.Id, out var n) ? sh.WithVarOffset(n) : sh)];
            // Hand-placed tabs, saved per shape.
            var tabs = (root["shapes"]?.AsArray() ?? []).OfType<JsonNode>().Where(x => x["tabs"] is JsonArray)
                .ToDictionary(x => Guid.TryParse(x["id"]?.GetValue<string>(), out var g) ? g : Guid.Empty,
                    x => x["tabs"]!.AsArray().OfType<JsonArray>().Select(p => new Vec2(p[0]!.GetValue<double>(), p[1]!.GetValue<double>())).ToImmutableList());
            if (tabs.Count > 0) all = [.. all.Select(sh => sh is PathShape ps && tabs.TryGetValue(sh.Id, out var t) ? ps.WithTabs(t) : sh)];
            // Chosen start points.
            var starts = (root["shapes"]?.AsArray() ?? []).OfType<JsonNode>().Where(x => x["start"] is JsonArray { Count: 2 })
                .ToDictionary(x => Guid.TryParse(x["id"]?.GetValue<string>(), out var g) ? g : Guid.Empty,
                    x => new Vec2(x["start"]![0]!.GetValue<double>(), x["start"]![1]!.GetValue<double>()));
            if (starts.Count > 0) all = [.. all.Select(sh => sh is PathShape ps && starts.TryGetValue(sh.Id, out var p) ? ps.WithStart(p) : sh)];
            var guides = (root["guides"]?.AsArray() ?? []).OfType<JsonNode>()
                .Select(g => new Guide(g["vertical"]?.GetValue<bool>() ?? true, g["at"]?.GetValue<double>() ?? 0)).ToImmutableList();
            return new DocState(layers.ToImmutable(), all) { Vars = vars, Guides = guides, Curve = CurveFrom(root["curve"]) };
        }
        catch (Exception e) when (e is InvalidOperationException or FormatException or KeyNotFoundException or NullReferenceException)
        {
            throw new FormatException($"The project file is damaged ({e.Message}).");
        }
    }

    static JsonNode? CurveJson(CurvedSurface c)
    {
        if (c == new CurvedSurface()) return null;
        return new JsonObject
        {
            ["on"] = c.On,
            ["shape"] = c.Shape.ToString(),
            ["points"] = new JsonArray([.. c.Points.Select(p => (JsonNode)new JsonArray(p.At.X, p.At.Y, p.Height))]),
            ["area"] = c.Area.IsEmpty ? null : new JsonArray(c.Area.MinX, c.Area.MinY, c.Area.MaxX, c.Area.MaxY),
            ["smoothing"] = c.Smoothing,
            ["sides"] = c.Sides,
            ["bandMm"] = c.BandMm,
            ["typed"] = c.Typed,
            ["axisDeg"] = c.AxisDeg,
            ["axisPoint"] = new JsonArray(c.AxisPoint.X, c.AxisPoint.Y),
            ["topMm"] = c.TopMm,
            ["diameterMm"] = c.DiameterMm,
        };
    }

    static CurvedSurface CurveFrom(JsonNode? n)
    {
        if (n is not JsonObject o) return new();
        double D(string k, double d) => o[k] is JsonValue v && v.TryGetValue<double>(out var x) ? x : d;
        var area = o["area"] is JsonArray { Count: 4 } a ? new Rect2(a[0]!.GetValue<double>(), a[1]!.GetValue<double>(), a[2]!.GetValue<double>(), a[3]!.GetValue<double>()) : Rect2.Empty;
        var axis = o["axisPoint"] is JsonArray { Count: 2 } ap ? new Vec2(ap[0]!.GetValue<double>(), ap[1]!.GetValue<double>()) : new Vec2(110, 110);
        return new CurvedSurface
        {
            On = o["on"]?.GetValue<bool>() ?? false,
            Shape = Enum.TryParse<SurfaceShape>(o["shape"]?.GetValue<string>(), out var sh) ? sh : SurfaceShape.Freeform,
            Points = [.. (o["points"] as JsonArray ?? []).OfType<JsonArray>().Where(p => p.Count == 3)
                .Select(p => new SurfacePoint(new Vec2(p[0]!.GetValue<double>(), p[1]!.GetValue<double>()), p[2]!.GetValue<double>()))],
            Area = area,
            Smoothing = D("smoothing", 0.3),
            Sides = (int)D("sides", 12),
            BandMm = D("bandMm", 0.2),
            Typed = o["typed"]?.GetValue<bool>() ?? false,
            AxisDeg = D("axisDeg", 90),
            AxisPoint = axis,
            TopMm = D("topMm", 50),
            DiameterMm = D("diameterMm", 80),
        };
    }

    public static void Save(DocState s, string path)
    {
        // Write to a temp file then swap, so a crash never leaves a half-written project.
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, Serialize(s));
        File.Move(tmp, path, overwrite: true);
    }

    public static DocState Load(string path) => Deserialize(File.ReadAllText(path));

}
