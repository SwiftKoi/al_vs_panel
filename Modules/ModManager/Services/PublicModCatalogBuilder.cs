using AlegacyWebPanel.Modules.ModManager.Contracts;
using AlegacyWebPanel.Modules.ModManager.Persistence;

namespace AlegacyWebPanel.Modules.ModManager.Services;

/// <summary>
/// Projects the installed-mod snapshot onto what the public website may show. The rules live here, in one
/// place: only mods published on ModDB, and only those players need (client or both sides).
/// </summary>
public static class PublicModCatalogBuilder
{
    public const string ClientSide = "client";
    public const string BothSides = "both";

    public static PublicModCatalogDto Build(
        string serverId,
        string? gameVersion,
        DateTimeOffset generatedUtc,
        string modDbBaseUrl,
        IReadOnlyCollection<string> trustedDownloadHosts,
        IEnumerable<(InstalledModDto Mod, ModDbMod? ModDb)> installed)
    {
        var entries = installed.ToArray();
        var mods = new List<PublicModDto>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (mod, modDb) in entries)
        {
            if (mod.ModId is null || modDb is null || modDb.AssetId <= 0 ||
                mod.Status is ModStatus.NotOnModDb or ModStatus.Unidentified or ModStatus.CheckFailed)
            {
                continue;
            }

            if (NormalizeSide(mod.Side) is not { } side || !seen.Add(mod.ModId))
            {
                continue;
            }

            var (downloadUrl, downloadFileName) = ResolveDownload(mod.Version, modDb, modDbBaseUrl, trustedDownloadHosts);

            mods.Add(new PublicModDto(
                mod.ModId,
                string.IsNullOrWhiteSpace(modDb.Name) ? mod.Name : modDb.Name,
                mod.Version,
                side,
                mod.Description,
                mod.Authors,
                modDb.AssetId,
                // The numeric form is stable across alias renames and is the form the website stores.
                $"{modDbBaseUrl.TrimEnd('/')}/show/mod/{modDb.AssetId}",
                mod.LogoUrl,
                downloadUrl,
                downloadFileName));
        }

        var complete = entries.All(entry => entry.Mod.Status != ModStatus.CheckFailed);
        return new PublicModCatalogDto(
            serverId,
            gameVersion,
            generatedUtc,
            complete,
            mods.OrderBy(mod => mod.Name, StringComparer.CurrentCultureIgnoreCase).ToArray());
    }

    /// <summary>
    /// The ModDB release that matches the installed version exactly, as a trusted https URL. Anything
    /// else (no such release, no file, a host outside the trusted list) gives no download rather than
    /// a different version or an unvetted link.
    /// </summary>
    private static (string? Url, string? FileName) ResolveDownload(
        string? installedVersion,
        ModDbMod modDb,
        string modDbBaseUrl,
        IReadOnlyCollection<string> trustedDownloadHosts)
    {
        if (string.IsNullOrWhiteSpace(installedVersion))
        {
            return (null, null);
        }

        var release = modDb.Releases.FirstOrDefault(candidate =>
            string.Equals(candidate.Version.Trim(), installedVersion.Trim(), StringComparison.OrdinalIgnoreCase));
        if (release is null || string.IsNullOrWhiteSpace(release.MainFileUrl) || string.IsNullOrWhiteSpace(release.FileName))
        {
            return (null, null);
        }

        var fileName = Path.GetFileName(release.FileName.Trim());
        if (fileName.Length == 0 ||
            !Uri.TryCreate(new Uri(modDbBaseUrl), release.MainFileUrl.Trim(), out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !trustedDownloadHosts.Any(host => uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase)))
        {
            return (null, null);
        }

        return (uri.ToString(), fileName);
    }

    /// <summary>
    /// <c>modinfo.json</c> says <c>universal</c> where ModDB says <c>both</c>. A mod with no side at all
    /// is universal, as in the game. Server-side and unrecognised values are not public (fail closed).
    /// </summary>
    public static string? NormalizeSide(string? side) =>
        side?.Trim().ToLowerInvariant() switch
        {
            null or "" => BothSides,
            "universal" or "both" => BothSides,
            "client" => ClientSide,
            _ => null
        };
}
