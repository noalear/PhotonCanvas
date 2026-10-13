using System.Text.Json;
using System.Text.Json.Nodes;

namespace PhotonCanvas.Core.Camera;

/// <summary>
/// Saved calibrations (the measured marks, refitted on load) in a JSON file.
/// One calibration per material distance; saving at an existing distance
/// (±0.25 mm) replaces it.
/// </summary>
public static class CalibrationStore
{
    public static string DefaultPath =>
        Path.Combine(AppIdentity.DataDir, "camera.json");

    public static List<CameraCalibration> Load(string path)
    {
        var o = new List<CameraCalibration>();
        try
        {
            if (!File.Exists(path)) return o;
            var root = JsonNode.Parse(File.ReadAllText(path));
            foreach (var c in root?["calibrations"]?.AsArray() ?? [])
            {
                if (c is null) continue;
                var pts = c["points"]!.AsArray().Select(p => new CalibrationPoint(
                    new Vec2(p!["mx"]!.GetValue<double>(), p["my"]!.GetValue<double>()),
                    new Vec2(p["ix"]!.GetValue<double>(), p["iy"]!.GetValue<double>()))).ToList();
                try
                {
                    o.Add(CameraCalibration.Fit(pts, c["distanceMm"]!.GetValue<double>(),
                        DateTimeOffset.Parse(c["created"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture)));
                }
                catch (CalibrationException) { /* skip a damaged entry, keep the rest */ }
            }
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException or InvalidOperationException or NullReferenceException or FormatException)
        {
            // An unreadable file means "not calibrated"; never stop the app.
        }
        return o;
    }

    public static void Save(string path, IEnumerable<CameraCalibration> cals)
    {
        var arr = new JsonArray();
        foreach (var c in cals.OrderBy(c => c.DistanceMm))
        {
            var pts = new JsonArray();
            foreach (var p in c.Points)
                pts.Add(new JsonObject { ["mx"] = p.Mm.X, ["my"] = p.Mm.Y, ["ix"] = p.Image.X, ["iy"] = p.Image.Y });
            arr.Add(new JsonObject
            {
                ["distanceMm"] = c.DistanceMm,
                ["created"] = c.Created.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                ["rmsMm"] = Math.Round(c.RmsMm, 4),
                ["points"] = pts,
            });
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, new JsonObject { ["version"] = 1, ["calibrations"] = arr }.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Add a calibration, replacing any at the same distance.</summary>
    public static List<CameraCalibration> With(IEnumerable<CameraCalibration> cals, CameraCalibration add) =>
        [.. cals.Where(c => Math.Abs(c.DistanceMm - add.DistanceMm) > 0.25), add];
}
