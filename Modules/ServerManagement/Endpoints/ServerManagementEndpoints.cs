using System.Security.Claims;
using AlegacyWebPanel.Core.Authorization;
using System.Text.Json;
using System.Text;
using AlegacyWebPanel.Core.Errors;
using AlegacyWebPanel.Modules.ServerManagement.Contracts;
using AlegacyWebPanel.Modules.ServerManagement.Exceptions;
using AlegacyWebPanel.Modules.ServerManagement.Services;

namespace AlegacyWebPanel.Modules.ServerManagement.Endpoints;

public static class ServerManagementEndpoints
{
    public static async Task<IResult> ListAsync(
        IServerManagementService service,
        CancellationToken cancellationToken) =>
        Results.Ok(await service.ListAsync(cancellationToken));

    public static async Task<IResult> StatusAsync(
        string serverId,
        IServerManagementService service,
        CancellationToken cancellationToken) =>
        await TranslateAsync(() => service.GetStatusAsync(serverId, cancellationToken));

    public static async Task<IResult> LifecycleAsync(
        string serverId,
        ServerLifecycleAction action,
        IServerManagementService service,
        CancellationToken cancellationToken) =>
        await TranslateAsync(() => service.ExecuteLifecycleAsync(serverId, action, cancellationToken));

    public static async Task<IResult> CommandAsync(
        string serverId,
        SendServerCommandRequest request,
        IServerManagementService service,
        CancellationToken cancellationToken) =>
        await TranslateAsync(() => service.SendCommandAsync(serverId, request.Command, cancellationToken));

    public static async Task<IResult> SetGameModeAsync(
        string serverId,
        SetGameModeRequest request,
        IServerActionsService service,
        CancellationToken cancellationToken) =>
        await TranslateAsync(() => service.SetGameModeAsync(serverId, request, cancellationToken));

    public static async Task<IResult> TeleportAsync(
        string serverId,
        TeleportRequest request,
        IServerActionsService service,
        CancellationToken cancellationToken) =>
        await TranslateAsync(() => service.TeleportAsync(serverId, request, cancellationToken));

    public static async Task<IResult> WarnAsync(
        string serverId,
        PlayerReasonRequest request,
        IServerActionsService service,
        CancellationToken cancellationToken) =>
        await TranslateAsync(() => service.WarnAsync(serverId, request, cancellationToken));

    public static async Task<IResult> KickAsync(
        string serverId,
        PlayerReasonRequest request,
        IServerActionsService service,
        CancellationToken cancellationToken) =>
        await TranslateAsync(() => service.KickAsync(serverId, request, cancellationToken));

    public static async Task<IResult> BanAsync(
        string serverId,
        PlayerReasonRequest request,
        IServerActionsService service,
        CancellationToken cancellationToken) =>
        await TranslateAsync(() => service.BanAsync(serverId, request, cancellationToken));

    public static async Task<IResult> HardBanAsync(
        string serverId,
        PlayerRequest request,
        IServerActionsService service,
        CancellationToken cancellationToken) =>
        await TranslateAsync(() => service.HardBanAsync(serverId, request, cancellationToken));

    public static async Task<IResult> LandClaimAsync(
        string serverId,
        LandClaimRequest request,
        IServerActionsService service,
        CancellationToken cancellationToken) =>
        await TranslateAsync(() => service.SetLandClaimAsync(serverId, request, cancellationToken));

    public static async Task<IResult> AllowClassReselectAsync(
        string serverId,
        PlayerRequest request,
        IServerActionsService service,
        CancellationToken cancellationToken) =>
        await TranslateAsync(() => service.AllowClassReselectAsync(serverId, request, cancellationToken));

    public static async Task<IResult> UnbanAsync(
        string serverId,
        PlayerRequest request,
        IServerActionsService service,
        CancellationToken cancellationToken) =>
        await TranslateAsync(() => service.UnbanAsync(serverId, request, cancellationToken));

    public static async Task<IResult> MetricsAsync(
        string serverId,
        IServerManagementService service,
        CancellationToken cancellationToken) =>
        await TranslateAsync(() => service.GetMetricsAsync(serverId, cancellationToken));

    public static async Task<IResult> ConnectionsAsync(
        string serverId,
        IServerManagementService service,
        ClaimsPrincipal user,
        CancellationToken cancellationToken) =>
        await TranslateAsync(async () =>
        {
            var response = await service.GetConnectionsAsync(serverId, cancellationToken);
            // Players' IP addresses are for admins only; moderators see names and quality.
            return user.IsInRole(PanelRoles.Admin)
                ? response
                : response with { Connections = response.Connections.Select(c => c with { RemoteAddress = string.Empty, RemotePort = 0 }).ToList() };
        });

    public static async Task<IResult> LogsAsync(
        string serverId,
        IServerManagementService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var events = await service.OpenLogStreamAsync(serverId, cancellationToken);
            return Results.Stream(async stream =>
            {
                await foreach (var logEvent in events.WithCancellation(cancellationToken))
                {
                    var eventName = logEvent.Kind.ToString().ToLowerInvariant();
                    var data = JsonSerializer.Serialize(logEvent.Data);
                    await stream.WriteAsync(
                        Encoding.UTF8.GetBytes($"event: {eventName}\ndata: {data}\n\n"),
                        cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                }
            }, "text/event-stream");
        }
        catch (ServerNotFoundException exception)
        {
            throw NotFound(exception);
        }
    }

    private static async Task<IResult> TranslateAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return Results.Ok(await operation());
        }
        catch (ServerNotFoundException exception)
        {
            throw NotFound(exception);
        }
        catch (InvalidServerCommandException exception)
        {
            throw new HttpException(StatusCodes.Status400BadRequest, "Invalid server command", exception.Message);
        }
        catch (ServerOperationConflictException exception)
        {
            throw new HttpException(StatusCodes.Status409Conflict, "Server operation conflict", exception.Message);
        }
        catch (ServerUnavailableException exception)
        {
            throw new HttpException(StatusCodes.Status503ServiceUnavailable, "Server unavailable", exception.Message);
        }
        catch (ServerOperationFailedException exception)
        {
            throw new HttpException(StatusCodes.Status502BadGateway, "Server operation failed", exception.Message);
        }
        catch (InvalidServerMetricsException exception)
        {
            throw new HttpException(StatusCodes.Status502BadGateway, "Invalid server metrics", exception.Message);
        }
        catch (InvalidServerConnectionsException exception)
        {
            throw new HttpException(StatusCodes.Status502BadGateway, "Invalid server connections", exception.Message);
        }
    }

    private static HttpException NotFound(ServerNotFoundException exception) =>
        new(StatusCodes.Status404NotFound, "Server not found", exception.Message);
}
