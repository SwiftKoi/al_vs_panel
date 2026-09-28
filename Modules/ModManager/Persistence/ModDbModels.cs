namespace AlegacyWebPanel.Modules.ModManager.Persistence;

public sealed record ModDbMod(
    string ModId,
    int AssetId,
    string Name,
    string? UrlAlias,
    string? Side,
    string? Author,
    string? LogoUrl,
    string? HomepageUrl,
    string? SourceUrl,
    string? IssueTrackerUrl,
    int? Downloads,
    IReadOnlyList<ModDbRelease> Releases)
{
    public string PageUrl(string baseUrl) =>
        string.IsNullOrWhiteSpace(UrlAlias) ? $"{baseUrl}/show/mod/{AssetId}" : $"{baseUrl}/{UrlAlias}";
}

public sealed record ModDbRelease(
    string Version,
    DateTimeOffset? CreatedUtc,
    int Downloads,
    string? FileName,
    string? MainFileUrl,
    IReadOnlyList<string> GameVersions,
    string? ChangelogHtml);

/// <summary>Result of a ModDB lookup: the mod, "not published" (Mod is null, no error), or a failure.</summary>
public sealed record ModDbLookup(ModDbMod? Mod, string? Error);
