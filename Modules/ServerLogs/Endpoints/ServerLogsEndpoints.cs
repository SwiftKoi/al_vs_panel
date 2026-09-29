using System.Security.Claims;
using AlegacyWebPanel.Core.Errors;
using AlegacyWebPanel.Modules.ServerLogs.Contracts;
using AlegacyWebPanel.Modules.ServerLogs.Exceptions;
using AlegacyWebPanel.Modules.ServerLogs.Services;

namespace AlegacyWebPanel.Modules.ServerLogs.Endpoints;

public static class ServerLogsEndpoints
{
    public static Task<IResult> StatusAsync(string serverId, IServerLogService service, CancellationToken cancellationToken) =>
        TranslateAsync(async () => Results.Ok(await service.StatusAsync(serverId, cancellationToken)));

    public static Task<IResult> SearchAsync(
        string serverId, string? q, DateTimeOffset? from, DateTimeOffset? to, bool? noise, string? cursor, int? limit,
        IServerLogService service, CancellationToken cancellationToken) =>
        TranslateAsync(async () => Results.Ok(await service.SearchAsync(serverId, new LogSearchRequest(q, from, to, noise ?? false), cursor, limit, cancellationToken)));

    public static Task<IResult> FacetsAsync(
        string serverId, string? q, DateTimeOffset? from, DateTimeOffset? to, bool? noise,
        IServerLogService service, CancellationToken cancellationToken) =>
        TranslateAsync(async () => Results.Ok(await service.FacetsAsync(serverId, new LogSearchRequest(q, from, to, noise ?? false), cancellationToken)));

    public static Task<IResult> HistogramAsync(
        string serverId, string? q, DateTimeOffset? from, DateTimeOffset? to, bool? noise, int? buckets,
        IServerLogService service, CancellationToken cancellationToken) =>
        TranslateAsync(async () => Results.Ok(await service.HistogramAsync(serverId, new LogSearchRequest(q, from, to, noise ?? false), buckets, cancellationToken)));

    public static Task<IResult> ContextAsync(
        string serverId, long entryId, int? before, int? after, bool? noise, string? logs,
        IServerLogService service, CancellationToken cancellationToken) =>
        TranslateAsync(async () => Results.Ok(await service.ContextAsync(serverId, entryId, before, after, noise ?? false, logs, cancellationToken)));

    public static Task<IResult> SignaturesAsync(
        string serverId, DateTimeOffset? from, DateTimeOffset? to,
        IServerLogService service, CancellationToken cancellationToken) =>
        TranslateAsync(async () => Results.Ok(await service.SignaturesAsync(serverId, from, to, cancellationToken)));

    public static Task<IResult> SetMutedAsync(
        string serverId, long signatureId, LogMuteRequest request,
        IServerLogService service, CancellationToken cancellationToken) =>
        TranslateAsync(async () =>
        {
            await service.SetSignatureMutedAsync(serverId, signatureId, request.Muted, cancellationToken);
            return Results.NoContent();
        });

    public static Task<IResult> SuggestAsync(
        string serverId, string key, string? prefix, string? q, DateTimeOffset? from, DateTimeOffset? to,
        IServerLogInsightsService service, CancellationToken cancellationToken) =>
        TranslateAsync(async () => Results.Ok(await service.SuggestAsync(serverId, key, prefix, q, from, to, cancellationToken)));

    public static Task<IResult> ProblemSummaryAsync(string serverId, IServerLogInsightsService service, CancellationToken cancellationToken) =>
        TranslateAsync(async () => Results.Ok(await service.ProblemSummaryAsync(serverId, cancellationToken)));

    public static Task<IResult> PlayersAsync(
        string serverId, DateTimeOffset? from, DateTimeOffset? to,
        IServerLogInsightsService service, CancellationToken cancellationToken) =>
        TranslateAsync(async () => Results.Ok(await service.PlayersAsync(serverId, from, to, cancellationToken)));

    public static Task<IResult> PlayerActivityAsync(
        string serverId, string player, DateTimeOffset? from, DateTimeOffset? to,
        IServerLogInsightsService service, CancellationToken cancellationToken) =>
        TranslateAsync(async () => Results.Ok(await service.PlayerActivityAsync(serverId, player, from, to, cancellationToken)));

    public static Task<IResult> LocationAsync(
        string serverId, int x, int? y, int z, int? radius, DateTimeOffset? from, DateTimeOffset? to,
        IServerLogInsightsService service, CancellationToken cancellationToken) =>
        TranslateAsync(async () => Results.Ok(await service.LocationAsync(serverId, x, y, z, radius, from, to, cancellationToken)));

    public static Task<IResult> BootsAsync(
        string serverId, DateTimeOffset? from, DateTimeOffset? to,
        IServerLogInsightsService service, CancellationToken cancellationToken) =>
        TranslateAsync(async () => Results.Ok(await service.BootsAsync(serverId, from, to, cancellationToken)));

    public static Task<IResult> SavedSearchesAsync(
        string serverId, ClaimsPrincipal user, IServerLogInsightsService service, CancellationToken cancellationToken) =>
        TranslateAsync(async () => Results.Ok(await service.SavedSearchesAsync(UserId(user), serverId, cancellationToken)));

    public static Task<IResult> SaveSearchAsync(
        string serverId, SaveSearchRequest request, ClaimsPrincipal user,
        IServerLogInsightsService service, CancellationToken cancellationToken) =>
        TranslateAsync(async () => Results.Ok(await service.SaveSearchAsync(UserId(user), serverId, request, cancellationToken)));

    public static Task<IResult> DeleteSavedSearchAsync(
        string serverId, long id, ClaimsPrincipal user,
        IServerLogInsightsService service, CancellationToken cancellationToken) =>
        TranslateAsync(async () =>
        {
            await service.DeleteSavedSearchAsync(UserId(user), serverId, id, cancellationToken);
            return Results.NoContent();
        });

    public static IResult Export(
        string serverId, string? q, DateTimeOffset? from, DateTimeOffset? to, bool? noise, string? format,
        IServerLogService service, HttpContext context)
    {
        var extension = string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase) ? "csv" : "txt";
        var request = new LogSearchRequest(q, from, to, noise ?? false);
        // Validate before streaming starts, so a bad query still gets a proper problem response.
        try
        {
            LogQuery.Parse(q);
        }
        catch (LogQueryException exception)
        {
            throw new HttpException(StatusCodes.Status400BadRequest, "Invalid log query", exception.Message);
        }

        return Results.Stream(
            stream => service.ExportAsync(serverId, request, extension, stream, context.RequestAborted),
            extension == "csv" ? "text/csv; charset=utf-8" : "text/plain; charset=utf-8",
            $"{serverId}-logs-{DateTime.UtcNow:yyyyMMdd-HHmmss}.{extension}");
    }

    private static string UserId(ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.Name)
        ?? throw new HttpException(StatusCodes.Status401Unauthorized, "Not signed in", "The request has no user.");

    private static async Task<IResult> TranslateAsync(Func<Task<IResult>> operation)
    {
        try
        {
            return await operation();
        }
        catch (ServerLogsNotConfiguredException exception)
        {
            throw new HttpException(StatusCodes.Status404NotFound, "Server logs not configured", exception.Message);
        }
        catch (SavedSearchNotFoundException exception)
        {
            throw new HttpException(StatusCodes.Status404NotFound, "Saved search not found", exception.Message);
        }
        catch (LogEntryNotFoundException exception)
        {
            throw new HttpException(StatusCodes.Status404NotFound, "Log entry not found", exception.Message);
        }
        catch (LogQueryException exception)
        {
            throw new HttpException(StatusCodes.Status400BadRequest, "Invalid log query", exception.Message);
        }
    }
}
