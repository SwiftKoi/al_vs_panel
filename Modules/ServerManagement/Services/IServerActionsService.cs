using AlegacyWebPanel.Modules.ServerManagement.Contracts;

namespace AlegacyWebPanel.Modules.ServerManagement.Services;

/// <summary>
/// Fixed console commands that moderators may run. Each command is built from validated parts,
/// so callers never pass raw console text.
/// </summary>
public interface IServerActionsService
{
    Task<SendServerCommandResponse> SetGameModeAsync(string serverId, SetGameModeRequest request, CancellationToken cancellationToken);
    Task<SendServerCommandResponse> TeleportAsync(string serverId, TeleportRequest request, CancellationToken cancellationToken);
    Task<SendServerCommandResponse> WarnAsync(string serverId, PlayerReasonRequest request, CancellationToken cancellationToken);
    Task<SendServerCommandResponse> KickAsync(string serverId, PlayerReasonRequest request, CancellationToken cancellationToken);
    Task<SendServerCommandResponse> BanAsync(string serverId, PlayerReasonRequest request, CancellationToken cancellationToken);
    Task<SendServerCommandResponse> HardBanAsync(string serverId, PlayerRequest request, CancellationToken cancellationToken);
    Task<SendServerCommandResponse> SetLandClaimAsync(string serverId, LandClaimRequest request, CancellationToken cancellationToken);
    Task<SendServerCommandResponse> AllowClassReselectAsync(string serverId, PlayerRequest request, CancellationToken cancellationToken);
    Task<SendServerCommandResponse> UnbanAsync(string serverId, PlayerRequest request, CancellationToken cancellationToken);
}
