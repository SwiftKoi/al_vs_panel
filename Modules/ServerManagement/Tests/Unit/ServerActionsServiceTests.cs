using AlegacyWebPanel.Modules.ServerManagement.Contracts;
using AlegacyWebPanel.Modules.ServerManagement.Exceptions;
using AlegacyWebPanel.Modules.ServerManagement.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace AlegacyWebPanel.ServerManagement.UnitTests;

public sealed class ServerActionsServiceTests
{
    private readonly RecordingServerManagement _server = new();
    private ServerActionsService Service => new(_server, NullLogger<ServerActionsService>.Instance);

    [Fact]
    public async Task Game_mode_builds_command()
    {
        await Service.SetGameModeAsync("main", new SetGameModeRequest("Flajakay", 1), CancellationToken.None);
        Assert.Equal(["/gamemode Flajakay 1"], _server.Commands);
    }

    [Theory]
    [InlineData(TeleportCoordinates.Pretty, "/tp Flajakay 10 -20.5 300")]
    [InlineData(TeleportCoordinates.Absolute, "/tp Flajakay =10 =-20.5 =300")]
    [InlineData(TeleportCoordinates.Relative, "/tp Flajakay ~10 ~-20.5 ~300")]
    public async Task Teleport_builds_command_for_each_coordinate_type(TeleportCoordinates coordinates, string expected)
    {
        await Service.TeleportAsync("main", new TeleportRequest("Flajakay", coordinates, 10, -20.5, 300), CancellationToken.None);
        Assert.Equal([expected], _server.Commands);
    }

    [Fact]
    public async Task Kick_and_ban_append_reason_and_unban_takes_only_name()
    {
        await Service.KickAsync("main", new PlayerReasonRequest("Flajakay", "  griefing the base "), CancellationToken.None);
        await Service.BanAsync("main", new PlayerReasonRequest("Flajakay", null), CancellationToken.None);
        await Service.UnbanAsync("main", new PlayerRequest("Flajakay"), CancellationToken.None);
        Assert.Equal(["/kick Flajakay griefing the base", "/ban Flajakay", "/unban Flajakay"], _server.Commands);
    }

    [Fact]
    public async Task Warn_requires_reason()
    {
        await Assert.ThrowsAsync<InvalidServerCommandException>(() =>
            Service.WarnAsync("main", new PlayerReasonRequest("Flajakay", "  "), CancellationToken.None));
        await Service.WarnAsync("main", new PlayerReasonRequest("Flajakay", "stop spawn camping"), CancellationToken.None);
        Assert.Equal(["/warn Flajakay stop spawn camping"], _server.Commands);
    }

    [Fact]
    public async Task Hardban_land_claims_and_class_reselect_build_commands()
    {
        await Service.HardBanAsync("main", new PlayerRequest("Flajakay"), CancellationToken.None);
        await Service.SetLandClaimAsync("main", new LandClaimRequest("Flajakay", LandClaimSetting.Allowance, 500), CancellationToken.None);
        await Service.SetLandClaimAsync("main", new LandClaimRequest("Flajakay", LandClaimSetting.MaxAreas, 0), CancellationToken.None);
        await Service.AllowClassReselectAsync("main", new PlayerRequest("Flajakay"), CancellationToken.None);
        Assert.Equal([
            "/hardban Flajakay",
            "/player Flajakay landclaimallowance 500",
            "/player Flajakay landclaimmaxareas 0",
            "/player Flajakay allowcharselonce"
        ], _server.Commands);
    }

    [Fact]
    public async Task Land_claim_allowance_accepts_any_non_negative_int()
    {
        await Service.SetLandClaimAsync("main", new LandClaimRequest("Flajakay", LandClaimSetting.Allowance, int.MaxValue), CancellationToken.None);
        Assert.Equal([$"/player Flajakay landclaimallowance {int.MaxValue}"], _server.Commands);
    }

    [Theory]
    [InlineData(LandClaimSetting.Allowance, -1)]
    [InlineData(LandClaimSetting.MaxAreas, -1)]
    [InlineData(LandClaimSetting.MaxAreas, 10000)]
    public async Task Land_claim_value_out_of_range_is_rejected(LandClaimSetting setting, int value)
    {
        await Assert.ThrowsAsync<InvalidServerCommandException>(() =>
            Service.SetLandClaimAsync("main", new LandClaimRequest("Flajakay", setting, value), CancellationToken.None));
        Assert.Empty(_server.Commands);
    }

    [Theory]
    [InlineData("Flajakay 2")]
    [InlineData("Name;/stop")]
    [InlineData("")]
    public async Task Bad_player_names_are_rejected(string player)
    {
        await Assert.ThrowsAsync<InvalidServerCommandException>(() =>
            Service.KickAsync("main", new PlayerReasonRequest(player, null), CancellationToken.None));
        Assert.Empty(_server.Commands);
    }

    [Fact]
    public async Task Bad_mode_reason_and_coordinates_are_rejected()
    {
        await Assert.ThrowsAsync<InvalidServerCommandException>(() =>
            Service.SetGameModeAsync("main", new SetGameModeRequest("Flajakay", 3), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidServerCommandException>(() =>
            Service.BanAsync("main", new PlayerReasonRequest("Flajakay", "line\nbreak"), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidServerCommandException>(() =>
            Service.BanAsync("main", new PlayerReasonRequest("Flajakay", new string('x', 201)), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidServerCommandException>(() =>
            Service.TeleportAsync("main", new TeleportRequest("Flajakay", TeleportCoordinates.Pretty, double.NaN, 0, 0), CancellationToken.None));
        Assert.Empty(_server.Commands);
    }

    private sealed class RecordingServerManagement : IServerManagementService
    {
        public List<string> Commands { get; } = [];

        public Task<SendServerCommandResponse> SendCommandAsync(string serverId, string command, CancellationToken cancellationToken)
        {
            Commands.Add(command);
            return Task.FromResult(new SendServerCommandResponse(serverId, true));
        }

        public Task<IReadOnlyList<ServerSummary>> ListAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ServerStatusResponse> GetStatusAsync(string serverId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ServerLifecycleResponse> ExecuteLifecycleAsync(string serverId, ServerLifecycleAction action, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ServerMetricsResponse> GetMetricsAsync(string serverId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ServerConnectionsResponse> GetConnectionsAsync(string serverId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IAsyncEnumerable<ServerLogEvent>> OpenLogStreamAsync(string serverId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
