using System.Globalization;
using System.Text.RegularExpressions;
using AlegacyWebPanel.Modules.ServerManagement.Contracts;
using AlegacyWebPanel.Modules.ServerManagement.Exceptions;

namespace AlegacyWebPanel.Modules.ServerManagement.Services;

public sealed class ServerActionsService(
    IServerManagementService serverManagement,
    ILogger<ServerActionsService> logger) : IServerActionsService
{
    public const int MaximumReasonLength = 200;
    // The game has no limit on the extra allowance; extra areas are capped to keep typos harmless.
    public const int MaximumLandClaimAreas = 9999;
    private const double MaximumCoordinate = 100_000_000;

    // A player name is one console argument: no spaces, so it can't add arguments or commands.
    private static readonly Regex PlayerNamePattern = new(@"^[\p{L}\p{N}_.\-]{1,32}$", RegexOptions.CultureInvariant);

    public Task<SendServerCommandResponse> SetGameModeAsync(string serverId, SetGameModeRequest request, CancellationToken cancellationToken)
    {
        var player = ValidatePlayer(request.PlayerName);
        if (request.Mode is < 0 or > 2)
        {
            throw new InvalidServerCommandException("The game mode must be 0 (guest), 1 (survival) or 2 (creative).");
        }

        return SendAsync(serverId, $"/gamemode {player} {request.Mode}", cancellationToken);
    }

    public Task<SendServerCommandResponse> TeleportAsync(string serverId, TeleportRequest request, CancellationToken cancellationToken)
    {
        var player = ValidatePlayer(request.PlayerName);
        var prefix = request.Coordinates switch
        {
            TeleportCoordinates.Pretty => "",
            TeleportCoordinates.Absolute => "=",
            TeleportCoordinates.Relative => "~",
            _ => throw new InvalidServerCommandException("Unknown coordinate type.")
        };

        return SendAsync(
            serverId,
            $"/tp {player} {prefix}{Coordinate(request.X)} {prefix}{Coordinate(request.Y)} {prefix}{Coordinate(request.Z)}",
            cancellationToken);
    }

    public Task<SendServerCommandResponse> WarnAsync(string serverId, PlayerReasonRequest request, CancellationToken cancellationToken)
    {
        var player = ValidatePlayer(request.PlayerName);
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new InvalidServerCommandException("A warning needs a reason.");
        }

        return SendAsync(serverId, WithReason($"/warn {player}", request.Reason), cancellationToken);
    }

    public Task<SendServerCommandResponse> KickAsync(string serverId, PlayerReasonRequest request, CancellationToken cancellationToken) =>
        SendAsync(serverId, WithReason($"/kick {ValidatePlayer(request.PlayerName)}", request.Reason), cancellationToken);

    public Task<SendServerCommandResponse> BanAsync(string serverId, PlayerReasonRequest request, CancellationToken cancellationToken) =>
        SendAsync(serverId, WithReason($"/ban {ValidatePlayer(request.PlayerName)}", request.Reason), cancellationToken);

    public Task<SendServerCommandResponse> UnbanAsync(string serverId, PlayerRequest request, CancellationToken cancellationToken) =>
        SendAsync(serverId, $"/unban {ValidatePlayer(request.PlayerName)}", cancellationToken);

    public Task<SendServerCommandResponse> HardBanAsync(string serverId, PlayerRequest request, CancellationToken cancellationToken) =>
        SendAsync(serverId, $"/hardban {ValidatePlayer(request.PlayerName)}", cancellationToken);

    public Task<SendServerCommandResponse> SetLandClaimAsync(string serverId, LandClaimRequest request, CancellationToken cancellationToken)
    {
        var player = ValidatePlayer(request.PlayerName);
        var setting = request.Setting switch
        {
            LandClaimSetting.Allowance => "landclaimallowance",
            LandClaimSetting.MaxAreas => "landclaimmaxareas",
            _ => throw new InvalidServerCommandException("Unknown land claim setting.")
        };

        var maximum = request.Setting == LandClaimSetting.MaxAreas ? MaximumLandClaimAreas : int.MaxValue;
        if (request.Value < 0 || request.Value > maximum)
        {
            throw new InvalidServerCommandException($"The value must be between 0 and {maximum}.");
        }

        return SendAsync(serverId, $"/player {player} {setting} {request.Value}", cancellationToken);
    }

    public Task<SendServerCommandResponse> AllowClassReselectAsync(string serverId, PlayerRequest request, CancellationToken cancellationToken) =>
        SendAsync(serverId, $"/player {ValidatePlayer(request.PlayerName)} allowcharselonce", cancellationToken);

    private Task<SendServerCommandResponse> SendAsync(string serverId, string command, CancellationToken cancellationToken)
    {
        logger.LogInformation("Running action command {Command} on server {ServerId}", command, serverId);
        return serverManagement.SendCommandAsync(serverId, command, cancellationToken);
    }

    private static string ValidatePlayer(string? playerName)
    {
        var name = playerName?.Trim() ?? string.Empty;
        return PlayerNamePattern.IsMatch(name)
            ? name
            : throw new InvalidServerCommandException("The player name is not valid.");
    }

    private static string WithReason(string command, string? reason)
    {
        var text = reason?.Trim() ?? string.Empty;
        if (text.Length > MaximumReasonLength)
        {
            throw new InvalidServerCommandException($"The reason can be at most {MaximumReasonLength} characters.");
        }

        if (text.Any(char.IsControl))
        {
            throw new InvalidServerCommandException("The reason contains unsupported control characters.");
        }

        return text.Length == 0 ? command : $"{command} {text}";
    }

    private static string Coordinate(double value) =>
        double.IsFinite(value) && Math.Abs(value) <= MaximumCoordinate
            ? value.ToString("0.###", CultureInfo.InvariantCulture)
            : throw new InvalidServerCommandException("A coordinate is out of range.");
}
