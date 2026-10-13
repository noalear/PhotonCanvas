using System.Text.Json;
using System.Text.Json.Nodes;

namespace YTool.Device;

/// <summary>
/// Pairing tokens cached per device IP in a small JSON file
/// (default <c>%APPDATA%\YTool\state.json</c>, shape <c>{"tokens":{"&lt;ip&gt;":"&lt;token&gt;"}}</c>).
/// Unknown keys in the file are preserved. Safe to call from several threads.
/// </summary>
public static class TokenStore
{
    static readonly Lock Gate = new();

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "YTool", "state.json");

    public static string? GetToken(string path, string ip)
    {
        lock (Gate)
            return Json.Str(Load(path)["tokens"] as JsonObject, ip);
    }

    public static void SaveToken(string path, string ip, string token)
    {
        lock (Gate)
        {
            var state = Load(path);
            if (state["tokens"] is not JsonObject tokens)
                state["tokens"] = tokens = [];
            tokens[ip] = token;
            string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (dir is not null)
                Directory.CreateDirectory(dir);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, state.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, path, overwrite: true);
        }
    }

    static JsonObject Load(string path)
    {
        try
        {
            return JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }
}
