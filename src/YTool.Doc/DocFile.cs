using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using YTool.Core;
using YTool.Core.Imaging;

namespace YTool.Doc;

/// <summary>
/// The <c>.ytool</c> project file: readable JSON, one entry per layer and
/// shape, geometry as SVG path data in the shape's local space.
/// </summary>
public static class DocFile
{
    public const string Extension = ".ytool";
    const int Version = 1;

    public static string Serialize(DocState s)
    {
        var layers = new JsonArray();
        foreach (var l in s.Layers)
            layers.Add(new JsonObject
            {
                ["id"] = l.Id.ToString(),
                ["name"] = l.Name,
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
                    };
                    // The outline too, so the file opens identically even without the font installed.
                    o["d"] = tx.Geometry.ToSvgPathData();
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
                    };
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
            ["format"] = "ytool",
            ["version"] = Version,
            ["machine"] = MachineProfile.F2Ultra.DeviceCode,
            ["layers"] = layers,
            ["shapes"] = shapes,
        };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <exception cref="FormatException">The file is not a readable YTool project.</exception>
    public static DocState Deserialize(string json)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(json); }
        catch (JsonException e) { throw new FormatException($"The project file is damaged ({e.Message})."); }
        if (root?["format"]?.GetValue<string>() != "ytool") throw new FormatException("This is not a YTool project file.");
        int version = root["version"]?.GetValue<int>() ?? 0;
        if (version > Version) throw new FormatException($"This project was saved by a newer version of {AppIdentity.ProductName}.");

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
                });
            }
            if (layers.Count == 0) layers.Add(Layer.Default(0));
            var layerIds = layers.Select(l => l.Id).ToHashSet();

            var shapes = ImmutableList.CreateBuilder<Shape>();
            foreach (var n in root["shapes"]?.AsArray() ?? [])
            {
                var type = n?["type"]?.GetValue<string>();
                if (n is null || type is not ("path" or "image" or "text")) continue;
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
                        st?["bendDeg"]?.GetValue<double>() ?? 0);
                    var text = n["text"]?.GetValue<string>() ?? "";
                    var saved = PathGeometry.FromSvgPathData(n["d"]?.GetValue<string>() ?? "");
                    shapes.Add(new TextShape
                    {
                        Id = id, Name = name, LayerId = layer, Transform = transform, GroupId = group,
                        Text = text, Style = style,
                        // Use the saved outline so the design is unchanged even if the font is missing on this PC.
                        Geometry = saved.IsEmpty ? TextLayout.Build(text, style) : saved,
                    });
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
                        tone?["invert"]?.GetValue<bool>() ?? false),
                });
            }
            return new DocState(layers.ToImmutable(), shapes.ToImmutable());
        }
        catch (Exception e) when (e is InvalidOperationException or FormatException or KeyNotFoundException or NullReferenceException)
        {
            throw new FormatException($"The project file is damaged ({e.Message}).");
        }
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
