using System.Text;
using AlegacyWebPanel.Modules.ModManager.Contracts;
using AlegacyWebPanel.Modules.ModManager.Persistence;
using AlegacyWebPanel.Modules.ModManager.Services;

namespace AlegacyWebPanel.Modules.ModManager.Tests.Unit;

public sealed class ModVersionTests
{
    [Theory]
    [InlineData("1.0.0", "1.0.0", 0)]
    [InlineData("1.0", "1.0.0", 0)]
    [InlineData("v1.2.3", "1.2.3", 0)]
    [InlineData("1.10.0", "1.9.0", 1)]
    [InlineData("4.0.0-rc.9", "4.0.0-rc.10", -1)]
    [InlineData("4.0.0-rc.10", "4.0.0", -1)]
    [InlineData("1.0.0-pre.1", "1.0.0-rc.1", -1)]
    [InlineData("1.2.0+build5", "1.2.0", 0)]
    [InlineData("1.0.0.1", "1.0.0", 1)]
    public void Compare_OrdersVersions(string left, string right, int expected) =>
        Assert.Equal(expected, Math.Sign(ModVersion.Compare(left, right)));

    [Fact]
    public void TryParse_RejectsText() => Assert.False(ModVersion.TryParse("latest", out _));
}

public sealed class ModInfoParserTests
{
    [Fact]
    public void TryParse_AcceptsLenientJsonLikeTheGame()
    {
        const string json = "﻿{\n // comment\n ModID: 'carryon', \"Name\": \"Carry On\", version: \"1.8.0\", side: 'Universal', authors: ['a', 'b'],\n}";
        var info = ModInfoParser.TryParse(Encoding.UTF8.GetBytes(json));

        Assert.NotNull(info);
        Assert.Equal("carryon", info.ModId);
        Assert.Equal("Carry On", info.Name);
        Assert.Equal("1.8.0", info.Version);
        Assert.Equal("universal", info.Side);
        Assert.Equal(["a", "b"], info.Authors);
    }

    [Fact]
    public void TryParse_ReturnsNullWithoutModId() =>
        Assert.Null(ModInfoParser.TryParse(Encoding.UTF8.GetBytes("{\"name\": \"x\"}")));

    [Fact]
    public void TryParse_ReturnsNullForGarbage() =>
        Assert.Null(ModInfoParser.TryParse(Encoding.UTF8.GetBytes("not json")));
}

public sealed class ReleaseResolverTests
{
    private static readonly ModVersion Game = Parse("1.22.7");

    [Fact]
    public void Resolve_PicksNewestCompatibleStable()
    {
        var mod = Mod(
            Release("1.3.0", "1.23.0"),          // wrong minor
            Release("1.2.1-rc.1", "1.22.5"),     // pre-release
            Release("1.2.0", "1.22.0", "1.22.4"),
            Release("1.1.0", "1.22.0"),
            Release("1.0.0", "1.21.0"));

        var result = ReleaseResolver.Resolve("1.0.0", mod, Game);

        Assert.Equal(ModStatus.UpdateAvailable, result.Status);
        Assert.Equal("1.2.0", result.UpdateVersion);
        Assert.Equal("1.2.1-rc.1", result.PrereleaseVersion);
        Assert.Equal("1.3.0", result.LatestVersion);
        Assert.Equal(2, result.NewerReleaseCount);
    }

    [Fact]
    public void Resolve_StaysOnPrereleaseTrack()
    {
        var mod = Mod(Release("4.0.0-rc.11", "1.22.6"), Release("4.0.0-rc.10", "1.22.6"), Release("3.0.0", "1.22.0"));

        var result = ReleaseResolver.Resolve("4.0.0-rc.10", mod, Game);

        Assert.Equal(ModStatus.UpdateAvailable, result.Status);
        Assert.Equal("4.0.0-rc.11", result.UpdateVersion);
        Assert.Null(result.PrereleaseVersion);
    }

    [Fact]
    public void Resolve_DoesNotOfferReleaseForNewerGamePatch()
    {
        var mod = Mod(Release("2.0.0", "1.22.8"), Release("1.0.0", "1.22.0"));

        var result = ReleaseResolver.Resolve("1.0.0", mod, Game);

        Assert.Equal(ModStatus.UpToDate, result.Status);
        Assert.NotNull(result.StatusDetail);
    }

    [Fact]
    public void Resolve_ReportsInstalledAheadOfModDb()
    {
        var result = ReleaseResolver.Resolve("3.2.6", Mod(Release("3.2.5", "1.22.0")), Game);

        Assert.Equal(ModStatus.Ahead, result.Status);
    }

    [Fact]
    public void Resolve_ReportsNoCompatibleRelease()
    {
        var result = ReleaseResolver.Resolve("1.0.0", Mod(Release("1.0.0", "1.20.0")), Game);

        Assert.Equal(ModStatus.NoCompatibleRelease, result.Status);
    }

    [Fact]
    public void Resolve_WithUnknownGameVersion_TreatsEverythingAsCompatible()
    {
        var result = ReleaseResolver.Resolve("1.0.0", Mod(Release("1.1.0", "1.30.0")), null);

        Assert.Equal("1.1.0", result.UpdateVersion);
    }

    private static ModVersion Parse(string version) => ModVersion.TryParse(version, out var parsed) ? parsed : throw new ArgumentException(version);

    private static ModDbRelease Release(string version, params string[] tags) =>
        new(version, null, 0, $"mod_{version}.zip", $"https://moddbcdn.vintagestory.at/mod_{version}.zip", tags, null);

    private static ModDbMod Mod(params ModDbRelease[] releases) =>
        new("mod", 1, "Mod", "mod", "both", null, null, null, null, null, null, releases);
}

public sealed class ChangelogSanitizerTests
{
    [Fact]
    public void Sanitize_KeepsFormattingAndStripsScripts()
    {
        var result = new ChangelogSanitizer().Sanitize(
            "<ul><li onclick=\"x()\">Fixed <b>boats</b></li></ul><script>alert(1)</script><img src=x onerror=y><a href=\"javascript:z\">bad</a><a href=\"https://example.com\">ok</a>");

        Assert.NotNull(result);
        Assert.Contains("<li>Fixed <b>boats</b></li>", result);
        Assert.DoesNotContain("script", result);
        Assert.DoesNotContain("onerror", result);
        Assert.DoesNotContain("onclick", result);
        Assert.DoesNotContain("javascript", result);
        Assert.Contains("href=\"https://example.com\"", result);
        Assert.Contains("rel=\"noopener noreferrer nofollow\"", result);
    }

    [Fact]
    public void Sanitize_ReturnsNullForEmpty() => Assert.Null(new ChangelogSanitizer().Sanitize("  "));
}
