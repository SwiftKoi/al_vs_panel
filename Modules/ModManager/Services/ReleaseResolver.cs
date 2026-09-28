using AlegacyWebPanel.Modules.ModManager.Contracts;
using AlegacyWebPanel.Modules.ModManager.Persistence;

namespace AlegacyWebPanel.Modules.ModManager.Services;

public sealed record ResolvedMod(
    ModStatus Status,
    string? StatusDetail,
    string? UpdateVersion,
    string? PrereleaseVersion,
    string? LatestVersion,
    int NewerReleaseCount);

/// <summary>
/// Decides what an installed mod could be updated to. A release is compatible when one of its
/// game-version tags has the server's major.minor and is not newer than the server (a 1.22.0 mod
/// runs on 1.22.7; a 1.21 or 1.22.8 one is not offered). One-click updates stay on stable
/// releases unless the installed version is already a pre-release.
/// </summary>
public static class ReleaseResolver
{
    public static bool IsCompatible(ModDbRelease release, ModVersion? gameVersion)
    {
        if (gameVersion is null)
        {
            return true;
        }

        return release.GameVersions.Any(tag =>
            ModVersion.TryParse(tag, out var tagVersion) &&
            tagVersion.Major == gameVersion.Major &&
            tagVersion.Minor == gameVersion.Minor &&
            tagVersion.CompareTo(gameVersion) <= 0);
    }

    public static ResolvedMod Resolve(string? installedVersion, ModDbMod mod, ModVersion? gameVersion)
    {
        var releases = mod.Releases
            .Select(release => (Release: release, Parsed: ModVersion.TryParse(release.Version, out var parsed) ? parsed : null))
            .Where(item => item.Parsed is not null)
            .OrderByDescending(item => item.Parsed)
            .ToArray();

        if (releases.Length == 0)
        {
            return new ResolvedMod(ModStatus.NoCompatibleRelease, "ModDB lists no releases for this mod.", null, null, null, 0);
        }

        var latest = releases[0].Release.Version;
        if (!ModVersion.TryParse(installedVersion, out var installed))
        {
            return new ResolvedMod(ModStatus.Unidentified, "The installed version could not be read.", null, null, latest, 0);
        }

        var compatibleNewer = releases
            .Where(item => item.Parsed!.CompareTo(installed) > 0 && IsCompatible(item.Release, gameVersion))
            .ToArray();
        var stableTarget = compatibleNewer.FirstOrDefault(item => !item.Parsed!.IsPrerelease);
        var anyTarget = compatibleNewer.FirstOrDefault();
        var updateTarget = installed.IsPrerelease ? anyTarget : stableTarget;
        var prerelease = anyTarget.Parsed is { IsPrerelease: true } && anyTarget.Release != updateTarget.Release
            ? anyTarget.Release.Version
            : null;

        if (updateTarget.Release is not null)
        {
            return new ResolvedMod(ModStatus.UpdateAvailable, null, updateTarget.Release.Version, prerelease, latest,
                compatibleNewer.Count(item => item.Parsed!.CompareTo(updateTarget.Parsed) <= 0));
        }

        if (installed.CompareTo(releases[0].Parsed) > 0)
        {
            return new ResolvedMod(ModStatus.Ahead, $"Installed {installedVersion} is newer than the latest published {latest}.",
                null, prerelease, latest, 0);
        }

        var anyCompatible = releases.Any(item => IsCompatible(item.Release, gameVersion));
        if (!anyCompatible)
        {
            return new ResolvedMod(ModStatus.NoCompatibleRelease,
                $"No release on ModDB targets game version {gameVersion}.", null, null, latest, 0);
        }

        var detail = releases.Any(item => item.Parsed!.CompareTo(installed) > 0)
            ? $"Newer releases exist ({latest}) but none is marked compatible with {gameVersion}."
            : null;
        return new ResolvedMod(ModStatus.UpToDate, detail, null, prerelease, latest, 0);
    }
}
