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
    public void VersionsCompareByNumber(string a, string b, bool newer) => Assert.Equal(newer, Updates.Newer(a, b));

    [Fact]
    public void GitHubsLatestReleaseIsRead()
    {
        const string json = """
        {"tag_name":"v0.2.0","html_url":"https://github.com/noalear/PhotonCanvas-releases/releases/tag/v0.2.0","draft":false,
         "body":"Edge shaping, xTool presets.",
         "assets":[{"name":"PhotonCanvas-win-x64.zip","browser_download_url":"https://example/win.zip"},
                   {"name":"PhotonCanvas-osx-arm64.tar.gz","browser_download_url":"https://example/mac.tar.gz"}]}
        """;
        var r = Updates.Parse(json)!;
        Assert.Equal("0.2.0", r.Version);
        Assert.Equal("Edge shaping, xTool presets.", r.Notes);
        Assert.Equal(2, r.Files.Count);
        Assert.EndsWith("v0.2.0", r.PageUrl);
        Assert.Null(Updates.Parse("""{"message":"Not Found"}"""));
        Assert.Null(Updates.Parse("""{"tag_name":"v9","draft":true}"""));
        Assert.Null(Updates.Parse("not json"));
    }

    [Fact]
    public void TheBuildCarriesItsVersion() => Assert.Matches(@"^\d+\.\d+\.\d+", Updates.Current);
}
