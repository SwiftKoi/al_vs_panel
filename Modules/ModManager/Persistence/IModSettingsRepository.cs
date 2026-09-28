namespace AlegacyWebPanel.Modules.ModManager.Persistence;

/// <summary>Per-server mod preferences set by admins (currently: pinned mods excluded from "update all").</summary>
public interface IModSettingsRepository
{
    Task<IReadOnlySet<string>> GetPinnedAsync(string serverId, CancellationToken cancellationToken);
    Task SetPinnedAsync(string serverId, string modId, bool pinned, CancellationToken cancellationToken);
}
