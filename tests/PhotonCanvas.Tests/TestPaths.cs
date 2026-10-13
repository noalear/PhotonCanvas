namespace PhotonCanvas.Tests;

static class TestPaths
{
    /// <summary>The repository root (the folder holding PhotonCanvas.slnx).</summary>
    public static string Root { get; } = FindRoot();

    static string FindRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "PhotonCanvas.slnx"))) return d.FullName;
        throw new InvalidOperationException("repository root not found");
    }

    /// <summary>
    /// A file from captures/ (the user's machine exports; not in git), or null
    /// when absent so tests can skip.
    /// </summary>
    public static string? Capture(string relative)
    {
        var p = Path.Combine(Root, "captures", relative);
        return File.Exists(p) ? p : null;
    }
}
