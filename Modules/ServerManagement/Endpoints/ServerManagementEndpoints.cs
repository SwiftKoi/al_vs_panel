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

    public static async Task<IResult> MetricsAsync(
        string serverId,
        IServerManagementService service,
        CancellationToken cancellationToken) =>
        await TranslateAsync(() => service.GetMetricsAsync(serverId, cancellationToken));

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
    }

    private static HttpException NotFound(ServerNotFoundException exception) =>
        new(StatusCodes.Status404NotFound, "Server not found", exception.Message);
}
