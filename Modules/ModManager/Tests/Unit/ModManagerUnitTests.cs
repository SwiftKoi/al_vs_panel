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

public sealed class PublicModCatalogBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly string[] TrustedHosts = ["mods.vintagestory.at", "moddbcdn.vintagestory.at"];

    [Theory]
    [InlineData("universal", "both")]
    [InlineData("Universal", "both")]
    [InlineData("both", "both")]
    [InlineData("client", "client")]
    [InlineData(" Client ", "client")]
    [InlineData(null, "both")]
    [InlineData("", "both")]
    [InlineData("server", null)]
    [InlineData("Server", null)]
    [InlineData("something-new", null)]
    public void NormalizeSide_maps_modinfo_and_moddb_values(string? side, string? expected) =>
        Assert.Equal(expected, PublicModCatalogBuilder.NormalizeSide(side));

    [Fact]
    public void Build_keeps_client_and_both_and_drops_server_side_mods()
    {
        var catalog = Build(
            (Installed("clientmod", "client"), ModDb(1)),
            (Installed("bothmod", "universal"), ModDb(2)),
            (Installed("servermod", "server"), ModDb(3)));

        Assert.Equal(["bothmod", "clientmod"], catalog.Mods.Select(mod => mod.ModId).Order().ToArray());
        Assert.Equal("client", catalog.Mods.Single(mod => mod.ModId == "clientmod").Side);
        Assert.Equal("both", catalog.Mods.Single(mod => mod.ModId == "bothmod").Side);
        Assert.True(catalog.Complete);
    }

    [Fact]
    public void Build_falls_back_to_the_moddb_side_that_the_service_already_applied()
    {
        // ModManagerService sets Side = modinfo side ?? ModDB side, so a mod without a modinfo side arrives as "both".
        var catalog = Build((Installed("envelopes", "both"), ModDb(19957)));

        Assert.Equal("both", Assert.Single(catalog.Mods).Side);
    }

    [Theory]
    [InlineData(ModStatus.NotOnModDb)]
    [InlineData(ModStatus.Unidentified)]
    [InlineData(ModStatus.CheckFailed)]
    public void Build_drops_private_unreadable_and_unchecked_mods(ModStatus status)
    {
        var catalog = Build((Installed("mod", "both", status), ModDb(1)));

        Assert.Empty(catalog.Mods);
    }

    [Theory]
    [InlineData(ModStatus.UpToDate)]
    [InlineData(ModStatus.UpdateAvailable)]
    [InlineData(ModStatus.Ahead)]
    [InlineData(ModStatus.NoCompatibleRelease)]
    public void Build_keeps_mods_published_on_moddb_whatever_their_version_state(ModStatus status)
    {
        var catalog = Build((Installed("mod", "both", status), ModDb(1)));

        Assert.Single(catalog.Mods);
    }

    [Fact]
    public void Build_marks_the_catalog_incomplete_when_a_moddb_lookup_failed()
    {
        var catalog = Build(
            (Installed("ok", "both"), ModDb(1)),
            (Installed("flaky", "both", ModStatus.CheckFailed), null));

        Assert.False(catalog.Complete);
        Assert.Equal("ok", Assert.Single(catalog.Mods).ModId);
    }

    [Fact]
    public void Build_stays_complete_for_private_mods_because_they_are_a_stable_answer()
    {
        var catalog = Build(
            (Installed("ok", "both"), ModDb(1)),
            (Installed("private", "both", ModStatus.NotOnModDb), null));

        Assert.True(catalog.Complete);
    }

    [Fact]
    public void Build_uses_the_numeric_moddb_url_the_website_stores()
    {
        var catalog = Build((Installed("alegacyattire", "both"), ModDb(72123, urlAlias: "alegacyattire")));

        var mod = Assert.Single(catalog.Mods);
        Assert.Equal("https://mods.vintagestory.at/show/mod/72123", mod.ModDbUrl);
        Assert.Equal(72123, mod.ModDbAssetId);
    }

    [Fact]
    public void Build_lists_one_entry_per_mod_id_when_two_files_carry_the_same_mod()
    {
        var catalog = Build(
            (Installed("dup", "both", fileName: "dup_1.0.zip"), ModDb(1)),
            (Installed("dup", "both", fileName: "dup_1.1.zip"), ModDb(1)));

        Assert.Single(catalog.Mods);
    }

    [Fact]
    public void Build_offers_the_moddb_file_for_exactly_the_installed_version()
    {
        var catalog = Build((Installed("mod", "both"), ModDb(1, null, Release("1.1.0", "https://moddbcdn.vintagestory.at/new.zip", "mod_1.1.0.zip"), Release("1.0.0"))));

        var mod = Assert.Single(catalog.Mods);
        Assert.Equal("https://moddbcdn.vintagestory.at/mod_1.zip?dl=mod_1.zip", mod.DownloadUrl);
        Assert.Equal("mod_1.zip", mod.DownloadFileName);
    }

    [Fact]
    public void Build_offers_no_download_when_moddb_has_no_release_for_the_installed_version()
    {
        // A different (newer) release is not a substitute: the archive must match what the server runs.
        var catalog = Build((Installed("mod", "both"), ModDb(1, null, Release("2.0.0"))));

        var mod = Assert.Single(catalog.Mods);
        Assert.Null(mod.DownloadUrl);
        Assert.Null(mod.DownloadFileName);
    }

    [Theory]
    [InlineData("http://moddbcdn.vintagestory.at/mod_1.zip")]
    [InlineData("https://evil.example/mod_1.zip")]
    [InlineData("")]
    public void Build_offers_no_download_from_an_untrusted_or_missing_file_url(string file)
    {
        var catalog = Build((Installed("mod", "both"), ModDb(1, null, Release("1.0.0", file))));

        Assert.Null(Assert.Single(catalog.Mods).DownloadUrl);
    }

    [Fact]
    public void Build_resolves_a_relative_moddb_download_link_and_strips_path_from_the_file_name()
    {
        var catalog = Build((Installed("mod", "both"), ModDb(1, null, Release("1.0.0", "/download/12345/mod_1.zip", "../evil/mod_1.zip"))));

        var mod = Assert.Single(catalog.Mods);
        Assert.Equal("https://mods.vintagestory.at/download/12345/mod_1.zip", mod.DownloadUrl);
        Assert.Equal("mod_1.zip", mod.DownloadFileName);
    }

    [Fact]
    public void Build_does_not_expose_files_or_sizes()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(Build((Installed("mod", "both"), ModDb(1))));

        Assert.DoesNotContain("zip", json);
        Assert.DoesNotContain("SizeBytes", json);
    }

    private static PublicModCatalogDto Build(params (InstalledModDto Mod, ModDbMod? ModDb)[] installed) =>
        PublicModCatalogBuilder.Build("main", "1.22.7", Now, "https://mods.vintagestory.at/", TrustedHosts, installed);

    private static InstalledModDto Installed(
        string modId,
        string? side,
        ModStatus status = ModStatus.UpToDate,
        string? fileName = null) =>
        new(fileName ?? modId + ".zip", modId, modId, "1.0.0", side, "desc", ["author"], 1234, Now, status, null,
            false, null, null, null, 0, false, null, null, null);

    private static ModDbMod ModDb(int assetId, string? urlAlias = null, params ModDbRelease[] releases) =>
        new("mod", assetId, "ModDB name", urlAlias, "both", null, "https://moddbcdn.vintagestory.at/logo.png",
            null, null, null, null, releases);

    private static ModDbRelease Release(string version, string? file = "https://moddbcdn.vintagestory.at/mod_1.zip?dl=mod_1.zip", string? fileName = "mod_1.zip") =>
        new(version, null, 0, fileName, file, ["1.22.7"], null);
}
