using AlegacyWebPanel.Modules.ModManager.Contracts;

namespace AlegacyWebPanel.Modules.ModManager.Services;

public interface IModManagerService
{
    /// <summary>Installed mods with their ModDB status. <paramref name="refresh"/> bypasses the ModDB cache.</summary>
    Task<ModOverviewDto> GetOverviewAsync(string serverId, bool refresh, CancellationToken cancellationToken);

    /// <summary>One installed mod with its full ModDB version history and sanitised changelogs.</summary>
    Task<ModDetailDto> GetDetailAsync(string serverId, string modId, CancellationToken cancellationToken);

    Task SetPinnedAsync(string serverId, string modId, bool pinned, CancellationToken cancellationToken);

    /// <summary>
    /// Starts a background update: download and verify every requested release, stage them on the
    /// server, then swap them into Mods in one step (the replaced files become the rollback backup).
    /// Takes effect on the next server restart.
    /// </summary>
    Task<ModUpdateJobDto> StartUpdateAsync(string serverId, ModUpdateRequest request, CancellationToken cancellationToken);

    ModUpdateJobDto? GetCurrentJob(string serverId);

    /// <summary>Restores the files replaced by the last update and removes the ones it installed.</summary>
    Task<ModBackupDto> RollbackAsync(string serverId, CancellationToken cancellationToken);
}
