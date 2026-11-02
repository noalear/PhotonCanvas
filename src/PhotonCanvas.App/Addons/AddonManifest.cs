using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhotonCanvas.App.Addons;

/// <summary>An add-on's addon.json.</summary>
public sealed record AddonManifest
{
    /// <summary>Short, unique, lower case: "papercraft", "slicer".</summary>
    public required string Id { get; init; }
    /// <summary>As shown in the Add-ons window: "Fold-up papercraft".</summary>
    public required string Name { get; init; }
    public string Version { get; init; } = "1.0.0";
    /// <summary>One or two sentences on what it adds.</summary>
    public string Description { get; init; } = "";
    public string Author { get; init; } = "";
    /// <summary>"Make", "Picture", "Camera", "Laser", "Arrange"… (groups the Add-ons window).</summary>
    public string Category { get; init; } = "Other";
    /// <summary>The assembly file in the add-on's folder (default: the only .dll whose name starts with PhotonCanvas.Addon.).</summary>
    public string? Assembly { get; init; }
    /// <summary>The full name of the class implementing <see cref="IAddon"/> (default: the only one in the assembly).</summary>
    public string? Entry { get; init; }
    /// <summary>The add-on API version it was made for (see <see cref="AddonHost.CurrentApiVersion"/>).</summary>
    public int ApiVersion { get; init; } = 1;
    /// <summary>Other add-ons' ids that must be on first (their published services are used).</summary>
    public string[] Requires { get; init; } = [];
    /// <summary>On when first installed (true), or waits for the person to turn it on.</summary>
    public bool OnByDefault { get; init; } = true;
    /// <summary>The GitHub repository ("owner/name") whose latest release holds this add-on's newest .pcaddon (optional): installed add-ons that name one are kept up to date.</summary>
    public string? Updates { get; init; }

    /// <summary>Set by the program: the folder it was found in.</summary>
    [JsonIgnore] public string Folder { get; init; } = "";
    /// <summary>Set by the program: shipped with PhotonCanvas (can be turned off but not deleted).</summary>
    [JsonIgnore] public bool BuiltIn { get; init; }

    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public static AddonManifest Read(string folder, bool builtIn)
    {
        var file = Path.Combine(folder, "addon.json");
        var m = JsonSerializer.Deserialize<AddonManifest>(File.ReadAllText(file), Json) ?? throw new FormatException("addon.json is empty.");
        if (string.IsNullOrWhiteSpace(m.Id) || m.Id.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')))
            throw new FormatException($"addon.json: \"id\" must be letters, digits, - _ or . (got \"{m.Id}\").");
        return m with { Id = m.Id.ToLowerInvariant(), Folder = folder, BuiltIn = builtIn };
    }
}
