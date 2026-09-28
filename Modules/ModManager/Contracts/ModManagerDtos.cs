using System.Text.Json.Serialization;

namespace AlegacyWebPanel.Modules.ModManager.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<ModStatus>))]
public enum ModStatus
{
    /// <summary>A newer compatible release is available.</summary>
    UpdateAvailable,
    UpToDate,
    /// <summary>The installed version is newer than anything published on ModDB.</summary>
    Ahead,
    /// <summary>ModDB has releases, but none targets the server's game version.</summary>
    NoCompatibleRelease,
    /// <summary>The mod is not published on ModDB (private or server-only mod).</summary>
    NotOnModDb,
    /// <summary>ModDB could not be reached for this mod.</summary>
    CheckFailed,
    /// <summary>The file has no readable modinfo.json (code-only mods, broken archives).</summary>
    Unidentified
}

public sealed record ModReleaseDto(
    string Version,
    DateTimeOffset? CreatedUtc,
    int Downloads,
    string? FileName,
    IReadOnlyList<string> GameVersions,
    bool IsPrerelease,
    bool IsCompatible,
    string? ChangelogHtml);

public sealed record InstalledModDto(
    string FileName,
    string? ModId,
    string Name,
    string? Version,
    string? Side,
    string? Description,
    IReadOnlyList<string> Authors,
    long SizeBytes,
    DateTimeOffset ModifiedUtc,
    ModStatus Status,
    string? StatusDetail,
    bool IsPrerelease,
    /// <summary>Release chosen by one-click update (stable, or the pre-release track the mod is already on).</summary>
    string? UpdateVersion,
    /// <summary>A newer compatible pre-release, when the update target is a stable release or there is none.</summary>
    string? PrereleaseVersion,
    string? LatestVersion,
    int NewerReleaseCount,
    bool IsPinned,
    /// <summary>Whether the running server reported this mod as loaded; null when unknown.</summary>
    bool? IsLoaded,
    string? ModDbUrl,
    string? LogoUrl);

public sealed record ModBackupDto(
    string BatchId,
    DateTimeOffset AppliedUtc,
    IReadOnlyList<ModBackupItemDto> Items);

public sealed record ModBackupItemDto(string? OldFileName, string NewFileName);

public sealed record ModOverviewDto(
    string ServerId,
    string? GameVersion,
    bool GameVersionFromLog,
    DateTimeOffset CheckedUtc,
    bool RestartRequired,
    DateTimeOffset? ServerStartedUtc,
    ModBackupDto? LastUpdate,
    ModUpdateJobDto? CurrentJob,
    IReadOnlyList<InstalledModDto> Mods);

public sealed record ModDetailDto(
    InstalledModDto Mod,
    string? Author,
    string? HomepageUrl,
    string? SourceUrl,
    string? IssueTrackerUrl,
    int? TotalDownloads,
    IReadOnlyList<ModReleaseDto> Releases);

public sealed record ModUpdateRequest(IReadOnlyList<ModUpdateRequestItem> Items);

public sealed record ModUpdateRequestItem(string ModId, string Version);

public sealed record ModPinRequest(bool Pinned);

[JsonConverter(typeof(JsonStringEnumConverter<ModUpdateJobState>))]
public enum ModUpdateJobState
{
    Running,
    Succeeded,
    Failed
}

[JsonConverter(typeof(JsonStringEnumConverter<ModUpdateItemState>))]
public enum ModUpdateItemState
{
    Queued,
    Downloading,
    Verifying,
    Staged,
    Installed,
    Failed
}

public sealed record ModUpdateJobItemDto(
    string ModId,
    string Name,
    string? FromVersion,
    string ToVersion,
    ModUpdateItemState State,
    string? Error);

public sealed record ModUpdateJobDto(
    string JobId,
    string ServerId,
    ModUpdateJobState State,
    DateTimeOffset StartedUtc,
    DateTimeOffset? FinishedUtc,
    string? Error,
    IReadOnlyList<ModUpdateJobItemDto> Items);
