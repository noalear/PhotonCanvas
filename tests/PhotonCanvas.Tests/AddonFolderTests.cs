using PhotonCanvas.App.Addons;

namespace PhotonCanvas.Tests;

/// <summary>Which assembly an add-on's folder starts from, and that the build puts only an add-on's own there.</summary>
public class AddonFolderTests
{
    static string TempFolder(params string[] files)
    {
        var dir = Path.Combine(Path.GetTempPath(), "pc-addon-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        foreach (var f in files) File.WriteAllText(Path.Combine(dir, f), "");
        return dir;
    }

    static AddonManifest Manifest(string id, string folder, string? assembly = null) =>
        new() { Id = id, Name = id, Folder = folder, Assembly = assembly };

    [Fact]
    public void TheOnlyAddonDllIsTheOne()
    {
        var dir = TempFolder("PhotonCanvas.Addon.Walls.dll", "Clipper2Lib.dll");
        try { Assert.Equal("PhotonCanvas.Addon.Walls.dll", Path.GetFileName(Manifest("walls", dir).MainAssemblyFile())); }
        finally { Directory.Delete(dir, true); }
    }

    [Theory]
    [InlineData("mapterrain")]
    [InlineData("map-terrain")]
    public void WithAnotherAddonsDllBesideItTheOneNamedAfterTheIdStarts(string id)
    {
        // What Map to terrain's folder held: Buildings' Stairs and Roofs copied in beside it.
        var dir = TempFolder("PhotonCanvas.Addon.MapTerrain.dll", "PhotonCanvas.Addon.Roofs.dll", "PhotonCanvas.Addon.Stairs.dll");
        try { Assert.Equal("PhotonCanvas.Addon.MapTerrain.dll", Path.GetFileName(Manifest(id, dir).MainAssemblyFile())); }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void NamedAssemblyWins()
    {
        var dir = TempFolder("PhotonCanvas.Addon.A.dll", "PhotonCanvas.Addon.B.dll");
        try { Assert.Equal("PhotonCanvas.Addon.B.dll", Path.GetFileName(Manifest("a", dir, "PhotonCanvas.Addon.B.dll").MainAssemblyFile())); }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void SeveralAndNoneNamedAfterItSaysSoPlainly()
    {
        var dir = TempFolder("PhotonCanvas.Addon.A.dll", "PhotonCanvas.Addon.B.dll");
        try
        {
            var e = Assert.Throws<InvalidOperationException>(() => Manifest("c", dir).MainAssemblyFile());
            Assert.Contains("2 PhotonCanvas.Addon.*.dll", e.Message);
        }
        finally { Directory.Delete(dir, true); }
    }

    /// <summary>The add-ons as built beside the program (every add-on is built before these tests).</summary>
    static string BuiltAddons()
    {
        var bin = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));  // tests/PhotonCanvas.Tests/bin/<Configuration>/net10.0
        var config = bin.Parent!.Name;
        var repo = bin.Parent.Parent!.Parent!.Parent!.Parent!.FullName;
        return Path.Combine(repo, "src", "PhotonCanvas.App", "bin", config, "net10.0", "addons");
    }

    [Fact]
    public void EveryBuiltAddonFolderHoldsOnlyItsOwnAddonAssembly()
    {
        var addons = BuiltAddons();
        Assert.True(Directory.Exists(addons), $"No add-ons built at {addons}");
        var folders = Directory.GetDirectories(addons);
        Assert.NotEmpty(folders);
        foreach (var folder in folders)
        {
            var dlls = Directory.GetFiles(folder, "PhotonCanvas.Addon.*.dll").Select(Path.GetFileName).ToList();
            Assert.True(dlls.Count == 1, $"{Path.GetFileName(folder)} has {string.Join(", ", dlls)}");
            var m = AddonManifest.Read(folder, builtIn: true);
            Assert.Equal(dlls[0], Path.GetFileName(m.MainAssemblyFile()));
        }
    }
}
