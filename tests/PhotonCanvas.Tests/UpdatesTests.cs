using System.Formats.Tar;
using System.IO.Compression;
using PhotonCanvas.Core;
using Xunit;

namespace PhotonCanvas.Tests;

public class UpdatesTests
{
    [Theory]
    [InlineData("0.2.0", "0.1.0", true)]
    [InlineData("v1.10.0", "1.9.3", true)]   // numbers, not text
    [InlineData("1.2.0", "1.2.0", false)]
    [InlineData("1.2", "1.2.0", false)]      // missing parts are 0
    [InlineData("1.2.0-beta", "1.2.0", false)] // a beta comes before the release
    [InlineData("1.2.0", "1.2.0-beta", true)]
    [InlineData("0.1.0+3fa2c1", "0.1.0", false)] // build info after + is ignored
    [InlineData("0.1.12", "0.1.9", true)]     // the release workflow's run numbers
    public void VersionsCompareByNumber(string a, string b, bool newer) => Assert.Equal(newer, Updates.Newer(a, b));

    [Fact]
    public void GitHubsLatestReleaseIsRead()
    {
        const string json = """
        {"tag_name":"v0.2.0","html_url":"https://github.com/noalear/PhotonCanvas/releases/tag/v0.2.0","draft":false,
         "body":"Edge shaping, xTool presets.",
         "assets":[{"name":"PhotonCanvas-win-x64.zip","browser_download_url":"https://example/win.zip","url":"https://api.github.com/repos/noalear/PhotonCanvas/releases/assets/1","size":47000000},
                   {"name":"PhotonCanvas-osx-arm64.tar.gz","browser_download_url":"https://example/mac.tar.gz","url":"https://api.github.com/repos/noalear/PhotonCanvas/releases/assets/2"},
                   {"name":"SHA256SUMS","browser_download_url":"https://example/sums","url":"https://api.github.com/repos/noalear/PhotonCanvas/releases/assets/3"}]}
        """;
        var r = Updates.Parse(json)!;
        Assert.Equal("0.2.0", r.Version);
        Assert.Equal("Edge shaping, xTool presets.", r.Notes);
        Assert.Equal(3, r.Files.Count);
        Assert.EndsWith("v0.2.0", r.PageUrl);
        var win = r.For("win-x64")!;
        Assert.Equal("https://api.github.com/repos/noalear/PhotonCanvas/releases/assets/1", win.ApiUrl);
        Assert.Equal(47000000, win.Size);
        Assert.Equal("PhotonCanvas-osx-arm64.tar.gz", r.For("osx-arm64")!.Name);
        Assert.Null(r.For("linux-x64"));
        Assert.NotNull(r.Named(Updates.SumsFile));
        Assert.Null(Updates.Parse("""{"message":"Not Found"}"""));
        Assert.Null(Updates.Parse("""{"tag_name":"v9","draft":true}"""));
        Assert.Null(Updates.Parse("not json"));
        Assert.Null(Updates.Parse("[]"));
    }

    [Fact]
    public void AnAddonsFileIsFoundInItsRelease()
    {
        static Updates.Release With(params string[] names) => new("1.1.0", "", "", [.. names.Select(n => new Updates.Asset(n, "", "", 0))]);
        Assert.Equal("grain-1.1.0.pcaddon", With("SHA256SUMS", "other.pcaddon", "grain-1.1.0.pcaddon").AddonFile("grain")!.Name);
        Assert.Equal("whatever.pcaddon", With("SHA256SUMS", "whatever.pcaddon").AddonFile("grain")!.Name);
        Assert.Null(With("a.pcaddon", "b.pcaddon").AddonFile("grain"));
        Assert.Null(With("grain.zip").AddonFile("grain"));
    }

    [Fact]
    public void TheBuildCarriesItsVersion() => Assert.Matches(@"^\d+\.\d+\.\d+", Updates.Current);

    [Theory]
    [InlineData("noalear/PhotonCanvas", true)]
    [InlineData("some-one/my.addon_2", true)]
    [InlineData("a/..", false)]
    [InlineData("_x/y", false)]
    [InlineData("noalear", false)]
    [InlineData("noalear/PhotonCanvas/releases", false)]
    [InlineData("../x", false)]
    [InlineData("a/b?c=1", false)]
    [InlineData(null, false)]
    public void OnlyOwnerSlashNameIsARepository(string? repo, bool ok) => Assert.Equal(ok, Updates.IsRepo(repo));

    [Theory]
    [InlineData("https://api.github.com/repos/a/b/releases/assets/1", true)]
    [InlineData("https://objects.githubusercontent.com/github-production-release-asset/1", false)]
    [InlineData("https://github.com/a/b/releases/download/v1/x.zip", false)]
    [InlineData("http://api.github.com/repos/a/b", false)]
    public void TheAccessKeyOnlyGoesToGitHubsApi(string url, bool sent) => Assert.Equal(sent, Updates.SendsKeyTo(new Uri(url)));

    [Fact]
    public void ChecksumListsAreRead()
    {
        var a = new string('a', 64);
        var b = new string('B', 64);
        var sums = Updates.ParseSums($"{a}  PhotonCanvas-win-x64.zip\n{b} *dist/PhotonCanvas-linux-x64.tar.gz\r\nnot a line\n{a[..10]}  short.zip\n");
        Assert.Equal(2, sums.Count);
        Assert.Equal(a, sums["PhotonCanvas-win-x64.zip"]);
        Assert.Equal(b.ToLowerInvariant(), sums["photoncanvas-linux-x64.tar.gz"]);
    }

    [Fact]
    public void AFilesChecksumIsSha256()
    {
        using var t = new TempDir();
        var f = t.Write("x.txt", "abc");
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", Updates.Sha256(f));
    }

    [Fact]
    public void AMacBundleIsUpdatedWhole()
    {
        var sep = Path.DirectorySeparatorChar;
        var root = Path.Combine(Path.GetTempPath(), "x", "PhotonCanvas.app");
        Assert.Equal(root, UpdateInstall.RootFor(Path.Combine(root, "Contents", "MacOS") + sep));
        var plain = Path.Combine(Path.GetTempPath(), "x", "PhotonCanvas");
        Assert.Equal(plain, UpdateInstall.RootFor(plain + sep));
    }

    // ---- putting a version in ------------------------------------------------------------------

    /// <summary>A version's folder: the marker, the file list (as tools/publish.sh writes it) and the given files.</summary>
    static string Version(TempDir t, string name, params (string Rel, string Text)[] files)
    {
        var root = Path.Combine(t.Path, name, "PhotonCanvas");
        Directory.CreateDirectory(root);
        var all = files.Append((UpdateInstall.MarkerFile, """{"repo":"noalear/PhotonCanvas","platform":"win-x64"}""")).ToList();
        foreach (var (rel, text) in all)
        {
            var f = Path.Combine(root, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(f)!);
            File.WriteAllText(f, text);
        }
        File.WriteAllLines(Path.Combine(root, UpdateInstall.ListFile), all.Select(x => "./" + x.Item1.Replace('\\', '/')).Append("./" + UpdateInstall.ListFile));
        return root;
    }

    static string Read(string root, string rel) => File.ReadAllText(Path.Combine(root, rel));

    [Fact]
    public void ANewVersionReplacesTheOldOneAndDropsWhatItNoLongerHas()
    {
        using var t = new TempDir();
        var install = Version(t, "installed", ("PhotonCanvas.exe", "old exe"), ("Core.dll", "old core"), ("addons/gone/addon.json", "{}"), ("addons/kept/addon.json", "old"));
        File.WriteAllText(Path.Combine(install, "my-notes.txt"), "not the program's"); // not in the list: left alone
        var next = Version(t, "next", ("PhotonCanvas.exe", "new exe"), ("Core.dll", "new core"), ("New.dll", "new"), ("addons/kept/addon.json", "new"));

        int n = UpdateInstall.Apply(next, install);

        Assert.Equal(6, n);
        Assert.Equal("new exe", Read(install, "PhotonCanvas.exe"));
        Assert.Equal("new core", Read(install, "Core.dll"));
        Assert.Equal("new", Read(install, "New.dll"));
        Assert.Equal("new", Read(install, "addons/kept/addon.json"));
        Assert.False(File.Exists(Path.Combine(install, "addons", "gone", "addon.json"))); // so the dropped add-on isn't found
        Assert.Equal("not the program's", Read(install, "my-notes.txt"));
        Assert.Equal("old exe", Read(install, "PhotonCanvas.exe" + UpdateInstall.OldSuffix));

        Assert.Equal(6, UpdateInstall.CleanUp(install)); // exe, Core, kept/addon.json, gone/addon.json, update.json, the list
        Assert.Empty(Directory.GetFiles(install, "*" + UpdateInstall.OldSuffix, SearchOption.AllDirectories));
    }

    [Fact]
    public void AFailedSwapPutsEverythingBack()
    {
        using var t = new TempDir();
        var install = Version(t, "installed", ("PhotonCanvas.exe", "old exe"), ("Core.dll", "old core"));
        File.WriteAllText(Path.Combine(install, "blocker"), "a file where the new version wants a folder");
        var next = Version(t, "next", ("PhotonCanvas.exe", "new exe"), ("Core.dll", "new core"), ("blocker/inside.dll", "x"));

        Assert.ThrowsAny<IOException>(() => UpdateInstall.Apply(next, install));

        Assert.Equal("old exe", Read(install, "PhotonCanvas.exe"));
        Assert.Equal("old core", Read(install, "Core.dll"));
        Assert.Equal("a file where the new version wants a folder", Read(install, "blocker"));
        Assert.Empty(Directory.GetFiles(install, "*" + UpdateInstall.OldSuffix, SearchOption.AllDirectories));
    }

    [Fact]
    public void OnlyAPublishedVersionGoesIn()
    {
        using var t = new TempDir();
        var install = Version(t, "installed", ("PhotonCanvas.exe", "old"));
        var bare = Path.Combine(t.Path, "bare");
        Directory.CreateDirectory(bare);
        File.WriteAllText(Path.Combine(bare, "PhotonCanvas.exe"), "who knows");
        Assert.Throws<InvalidDataException>(() => UpdateInstall.Apply(bare, install));
        Assert.Equal("old", Read(install, "PhotonCanvas.exe"));
    }

    [Fact]
    public void AnOlderListCanOnlyNameFilesInsideTheFolder()
    {
        using var t = new TempDir();
        var install = Version(t, "installed", ("a.dll", "a"));
        File.AppendAllLines(Path.Combine(install, UpdateInstall.ListFile), ["../outside.txt", "/etc/passwd", "", "# note"]);
        var listed = UpdateInstall.ListedIn(install);
        Assert.Contains("a.dll", listed);
        Assert.DoesNotContain(listed, f => f.Contains("..") || f.Contains("passwd"));
    }

    [Fact]
    public void ZipAndTarDownloadsUnpack()
    {
        using var t = new TempDir();
        var src = Version(t, "src", ("PhotonCanvas.exe", "exe"), ("addons/a/addon.json", "{}"));
        var zip = Path.Combine(t.Path, "PhotonCanvas-win-x64.zip");
        ZipFile.CreateFromDirectory(Path.GetDirectoryName(src)!, zip);
        var fromZip = UpdateInstall.Unpack(zip, Path.Combine(t.Path, "z"));
        Assert.Equal("PhotonCanvas", Path.GetFileName(fromZip));
        Assert.Equal("exe", Read(fromZip, "PhotonCanvas.exe"));

        var tgz = Path.Combine(t.Path, "PhotonCanvas-linux-x64.tar.gz");
        using (var file = File.Create(tgz))
        using (var gz = new GZipStream(file, CompressionLevel.Fastest))
            TarFile.CreateFromDirectory(Path.GetDirectoryName(src)!, gz, includeBaseDirectory: false);
        var fromTar = UpdateInstall.Unpack(tgz, Path.Combine(t.Path, "t"));
        Assert.Equal("{}", Read(fromTar, "addons/a/addon.json"));
        Assert.NotNull(UpdateInstall.ReadMarker(UpdateInstall.ProgramDirIn(fromTar)));
    }

    [Fact]
    public void AFreshInstallNeverTakesOverSomeoneElsesFolder()
    {
        using var t = new TempDir();
        var next = Version(t, "next", ("PhotonCanvas.exe", "new"));
        var theirs = Path.Combine(t.Path, "theirs");
        Directory.CreateDirectory(theirs);
        File.WriteAllText(Path.Combine(theirs, "photo.jpg"), "mine");
        Assert.Throws<IOException>(() => UpdateInstall.InstallInto(next, theirs));
        Assert.Single(Directory.GetFiles(theirs));

        var fresh = Path.Combine(t.Path, "Programs", "PhotonCanvas");
        UpdateInstall.InstallInto(next, fresh);
        Assert.Equal("new", Read(fresh, "PhotonCanvas.exe"));

        var again = Version(t, "again", ("PhotonCanvas.exe", "newer"));
        UpdateInstall.InstallInto(again, fresh); // a published copy already there: updated
        Assert.Equal("newer", Read(fresh, "PhotonCanvas.exe"));
    }

    sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pc-update-" + Guid.NewGuid().ToString("N"));
        public TempDir() => Directory.CreateDirectory(Path);
        public string Write(string name, string text) { var f = System.IO.Path.Combine(Path, name); File.WriteAllText(f, text); return f; }
        public void Dispose() { try { Directory.Delete(Path, recursive: true); } catch (IOException) { } }
    }
}
