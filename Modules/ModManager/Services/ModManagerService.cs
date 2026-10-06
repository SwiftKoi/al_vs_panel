using AlegacyWebPanel.Modules.ModManager.Configuration;
using AlegacyWebPanel.Modules.ModManager.Contracts;
using AlegacyWebPanel.Modules.ModManager.Exceptions;
using AlegacyWebPanel.Modules.ModManager.Persistence;
using AlegacyWebPanel.Modules.ServerManagement.Services;
using Microsoft.Extensions.Options;

namespace AlegacyWebPanel.Modules.ModManager.Services;

public sealed class ModManagerService(
    IModTargetRepository target,
    IModDbRepository modDb,
    IModSettingsRepository settings,
    IChangelogSanitizer sanitizer,
    IModUpdateJobTracker jobs,
    IServerManagementService servers,
    IOptions<ModManagerOptions> options,
    TimeProvider timeProvider,
    ILogger<ModManagerService> logger) : IModManagerService
{
    private readonly ModManagerOptions _options = options.Value;

    public async Task<ModOverviewDto> GetOverviewAsync(string serverId, bool refresh, CancellationToken cancellationToken)
    {
        var snapshot = await LoadAsync(serverId, refresh, cancellationToken);
        var backup = await target.GetBackupAsync(snapshot.Operation, cancellationToken);
        var startedUtc = await GetServerStartAsync(serverId, cancellationToken);

        // Mods are read once at startup: anything written or swapped after that waits for a restart.
        var restartRequired = startedUtc is { } started &&
                              (snapshot.Files.Any(file => file.ModifiedUtc > started) || backup?.AppliedUtc > started);

        return new ModOverviewDto(
            serverId,
            snapshot.GameVersion?.Text,
            snapshot.GameVersionFromLog,
            timeProvider.GetUtcNow(),
            restartRequired,
            startedUtc,
            backup is null ? null : ToDto(backup),
            jobs.Get(serverId),
            snapshot.Mods.Select(mod => mod.Dto).ToArray());
    }

    public async Task<PublicModCatalogDto> GetPublicCatalogAsync(string serverId, CancellationToken cancellationToken)
    {
        var snapshot = await LoadAsync(serverId, refresh: false, cancellationToken);

        return PublicModCatalogBuilder.Build(
            serverId,
            snapshot.GameVersion?.Text,
            timeProvider.GetUtcNow(),
            _options.ModDbBaseUrl,
            _options.TrustedDownloadHosts,
            snapshot.Mods.Select(mod => (mod.Dto, mod.ModDb)));
    }

    public async Task<ModDetailDto> GetDetailAsync(string serverId, string modId, CancellationToken cancellationToken)
    {
        var snapshot = await LoadAsync(serverId, refresh: false, cancellationToken);
        var mod = snapshot.Mods.FirstOrDefault(item => string.Equals(item.Dto.ModId, modId, StringComparison.OrdinalIgnoreCase))
                  ?? throw new ModNotInstalledException(modId);

        var releases = mod.ModDb?.Releases
            .OrderByDescending(release => release.Version, Comparer<string>.Create(ModVersion.Compare))
            .Select(release => new ModReleaseDto(
                release.Version,
                release.CreatedUtc,
                release.Downloads,
                release.FileName,
                release.GameVersions,
                ModVersion.TryParse(release.Version, out var parsed) && parsed.IsPrerelease,
                ReleaseResolver.IsCompatible(release, snapshot.GameVersion),
                sanitizer.Sanitize(release.ChangelogHtml)))
            .ToArray() ?? [];

        return new ModDetailDto(
            mod.Dto,
            mod.ModDb?.Author,
            mod.ModDb?.HomepageUrl,
            mod.ModDb?.SourceUrl,
            mod.ModDb?.IssueTrackerUrl,
            mod.ModDb?.Downloads,
            releases);
    }

    public async Task SetPinnedAsync(string serverId, string modId, bool pinned, CancellationToken cancellationToken)
    {
        GetServerOptions(serverId);
        await settings.SetPinnedAsync(serverId, modId.Trim().ToLowerInvariant(), pinned, cancellationToken);
    }

    public async Task<ModUpdateJobDto> StartUpdateAsync(string serverId, ModUpdateRequest request, CancellationToken cancellationToken)
    {
        if (request.Items is not { Count: > 0 })
        {
            throw new ModValidationException("Choose at least one mod to update.");
        }

        if (jobs.Get(serverId) is { State: ModUpdateJobState.Running })
        {
            throw new ModUpdateConflictException();
        }

        var snapshot = await LoadAsync(serverId, refresh: false, cancellationToken);
        var plan = new List<ModUpdatePlanItem>();
        foreach (var item in request.Items.DistinctBy(item => item.ModId, StringComparer.OrdinalIgnoreCase))
        {
            var mod = snapshot.Mods.FirstOrDefault(entry => string.Equals(entry.Dto.ModId, item.ModId, StringComparison.OrdinalIgnoreCase))
                      ?? throw new ModNotInstalledException(item.ModId);
            if (mod.ModDb is null)
            {
                throw new ModValidationException($"'{mod.Dto.Name}' is not published on ModDB, so it cannot be updated from there.");
            }

            var release = mod.ModDb.Releases.FirstOrDefault(release => release.Version == item.Version)
                          ?? throw new ModReleaseNotFoundException(item.ModId, item.Version);
            if (string.IsNullOrWhiteSpace(release.MainFileUrl) || string.IsNullOrWhiteSpace(release.FileName))
            {
                throw new ModValidationException($"ModDB has no download for {mod.Dto.Name} {release.Version}.");
            }

            if (mod.Dto.Version == release.Version)
            {
                throw new ModValidationException($"{mod.Dto.Name} {release.Version} is already installed.");
            }

            plan.Add(new ModUpdatePlanItem(mod.Dto.ModId!, mod.Dto.Name, mod.Dto.FileName, mod.Dto.Version, release));
        }

        return jobs.Start(serverId, snapshot.Operation, plan)
               ?? throw new ModUpdateConflictException();
    }

    public ModUpdateJobDto? GetCurrentJob(string serverId)
    {
        GetServerOptions(serverId);
        return jobs.Get(serverId);
    }

    public async Task<ModBackupDto> RollbackAsync(string serverId, CancellationToken cancellationToken)
    {
        var operation = GetServerOptions(serverId).Operation;
        if (jobs.Get(serverId) is { State: ModUpdateJobState.Running })
        {
            throw new ModUpdateConflictException();
        }

        var journal = await target.RollbackAsync(operation, cancellationToken);
        logger.LogInformation("Rolled back mod update {BatchId} on {ServerId}", journal.BatchId, serverId);
        return ToDto(journal);
    }

    private async Task<Snapshot> LoadAsync(string serverId, bool refresh, CancellationToken cancellationToken)
    {
        var serverOptions = GetServerOptions(serverId);
        var filesTask = target.ScanAsync(serverOptions.Operation, cancellationToken);
        var gameInfoTask = target.GetGameInfoAsync(serverOptions.Operation, cancellationToken);
        var pinnedTask = settings.GetPinnedAsync(serverId, cancellationToken);
        await Task.WhenAll(filesTask, gameInfoTask, pinnedTask);
        var files = filesTask.Result;
        var gameInfo = gameInfoTask.Result;
        var pinned = pinnedTask.Result;

        var configuredVersion = serverOptions.GameVersion;
        ModVersion.TryParse(string.IsNullOrWhiteSpace(configuredVersion) ? gameInfo.GameVersion : configuredVersion, out var gameVersion);
        var loaded = gameInfo.LoadedMods?.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var infos = files.ToDictionary(file => file.FileName, file => file.ModInfo is null ? null : ModInfoParser.TryParse(file.ModInfo));
        var modIds = infos.Values.Where(info => info is not null).Select(info => info!.ModId).Distinct().ToArray();

        var lookups = new Dictionary<string, ModDbLookup>(StringComparer.OrdinalIgnoreCase);
        using (var gate = new SemaphoreSlim(Math.Max(1, _options.MaximumConcurrentRequests)))
        {
            await Task.WhenAll(modIds.Select(async modId =>
            {
                await gate.WaitAsync(cancellationToken);
                try
                {
                    var lookup = await modDb.GetModAsync(modId, refresh, cancellationToken);
                    lock (lookups)
                    {
                        lookups[modId] = lookup;
                    }
                }
                finally
                {
                    gate.Release();
                }
            }));
        }

        var mods = files.Select(file =>
        {
            var info = infos[file.FileName];
            ModDbLookup? lookup = info is null ? null : lookups.GetValueOrDefault(info.ModId);
            return BuildMod(file, info, lookup, gameVersion, pinned, loaded);
        }).OrderBy(mod => mod.Dto.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();

        return new Snapshot(serverOptions.Operation, gameVersion, string.IsNullOrWhiteSpace(configuredVersion), files, mods);
    }

    private SnapshotMod BuildMod(
        ModFileEntry file,
        ModInfo? info,
        ModDbLookup? lookup,
        ModVersion? gameVersion,
        IReadOnlySet<string> pinned,
        IReadOnlySet<string>? loaded)
    {
        if (info is null)
        {
            var detail = file.Error ?? (file.Kind == "code"
                ? "Single-file code mods carry no modinfo.json and cannot be checked."
                : "No readable modinfo.json at the archive root.");
            return new SnapshotMod(new InstalledModDto(
                file.FileName, null, Path.GetFileNameWithoutExtension(file.FileName), null, null, null, [],
                file.SizeBytes, file.ModifiedUtc, ModStatus.Unidentified, detail, false,
                null, null, null, 0, false, null, null, null), null);
        }

        var modDbMod = lookup?.Mod;
        var resolved = lookup switch
        {
            { Error: { } error } => new ResolvedMod(ModStatus.CheckFailed, error, null, null, null, 0),
            { Mod: null } or null => new ResolvedMod(ModStatus.NotOnModDb, null, null, null, null, 0),
            _ => ReleaseResolver.Resolve(info.Version, modDbMod!, gameVersion)
        };

        var dto = new InstalledModDto(
            file.FileName,
            info.ModId,
            info.Name,
            info.Version,
            info.Side ?? modDbMod?.Side,
            info.Description,
            info.Authors,
            file.SizeBytes,
            file.ModifiedUtc,
            resolved.Status,
            resolved.StatusDetail,
            ModVersion.TryParse(info.Version, out var installed) && installed.IsPrerelease,
            resolved.UpdateVersion,
            resolved.PrereleaseVersion,
            resolved.LatestVersion,
            resolved.NewerReleaseCount,
            pinned.Contains(info.ModId),
            loaded?.Contains(info.ModId),
            modDbMod?.PageUrl(_options.ModDbBaseUrl.TrimEnd('/')),
            modDbMod?.LogoUrl);
        return new SnapshotMod(dto, modDbMod);
    }

    private async Task<DateTimeOffset?> GetServerStartAsync(string serverId, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            var metrics = await servers.GetMetricsAsync(serverId, timeout.Token);
            return metrics.StartedAtUtc;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug(exception, "Could not read the start time of {ServerId}", serverId);
            return null;
        }
    }

    private ModManagerServerOptions GetServerOptions(string serverId) =>
        _options.Servers.TryGetValue(serverId, out var serverOptions) && !string.IsNullOrWhiteSpace(serverOptions.Operation)
            ? serverOptions
            : throw new ModServerNotFoundException(serverId);

    private static ModBackupDto ToDto(ModJournal journal) =>
        new(journal.BatchId, journal.AppliedUtc, journal.Items.Select(item => new ModBackupItemDto(item.Old, item.New)).ToArray());

    private sealed record Snapshot(
        string Operation,
        ModVersion? GameVersion,
        bool GameVersionFromLog,
        IReadOnlyList<ModFileEntry> Files,
        IReadOnlyList<SnapshotMod> Mods);

    private sealed record SnapshotMod(InstalledModDto Dto, ModDbMod? ModDb);
}
