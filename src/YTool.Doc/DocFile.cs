using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using YTool.Core;

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
            });
        var shapes = new JsonArray();
        foreach (var sh in s.Shapes.OfType<PathShape>())
        {
            var t = sh.Transform;
            shapes.Add(new JsonObject
            {
                ["id"] = sh.Id.ToString(),
                ["name"] = sh.Name,
                ["layer"] = sh.LayerId.ToString(),
                ["type"] = "path",
                ["d"] = sh.Geometry.ToSvgPathData(),
                ["transform"] = new JsonArray(t.A, t.B, t.C, t.D, t.E, t.F),
            });
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
        if (version > Version) throw new FormatException("This project was saved by a newer version of YTool Studio.");

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
                });
            }
            if (layers.Count == 0) layers.Add(Layer.Default(0));
            var layerIds = layers.Select(l => l.Id).ToHashSet();

            var shapes = ImmutableList.CreateBuilder<Shape>();
            foreach (var n in root["shapes"]?.AsArray() ?? [])
            {
                if (n?["type"]?.GetValue<string>() != "path") continue;
                var t = n["transform"]?.AsArray().Select(v => v!.GetValue<double>()).ToArray();
                var layer = Guid.TryParse(n["layer"]?.GetValue<string>(), out var lid) && layerIds.Contains(lid) ? lid : layers[0].Id;
                shapes.Add(new PathShape
                {
                    Id = Guid.TryParse(n["id"]?.GetValue<string>(), out var id) ? id : Guid.NewGuid(),
                    Name = n["name"]?.GetValue<string>() ?? "",
                    LayerId = layer,
                    Geometry = PathGeometry.FromSvgPathData(n["d"]?.GetValue<string>() ?? ""),
                    Transform = t is { Length: 6 } ? new Affine(t[0], t[1], t[2], t[3], t[4], t[5]) : Affine.Identity,
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
