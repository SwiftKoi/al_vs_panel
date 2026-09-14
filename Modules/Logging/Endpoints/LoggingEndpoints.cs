using AlegacyWebPanel.Core.Errors;
using AlegacyWebPanel.Modules.Logging.Contracts;
using AlegacyWebPanel.Modules.Logging.Exceptions;
using AlegacyWebPanel.Modules.Logging.Services;

namespace AlegacyWebPanel.Modules.Logging.Endpoints;

public static class LoggingEndpoints
{
    public static async Task<IResult> QueryAsync(
        string? level,
        string? source,
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? search,
        int? limit,
        int? offset,
        ILoggingService service,
        CancellationToken cancellationToken) =>
        await TranslateAsync(async () =>
        {
            var query = new LogQuery(
                MinimumLevel: ParseLevel(level),
                Source: Normalize(source),
                From: from,
                To: to,
                Search: Normalize(search),
                Limit: limit ?? 100,
                Offset: offset ?? 0);
            return Results.Ok(await service.QueryAsync(query, cancellationToken));
        });

    public static async Task<IResult> SourcesAsync(
        ILoggingService service,
        CancellationToken cancellationToken) =>
        await TranslateAsync(async () => Results.Ok(await service.ListSourcesAsync(cancellationToken)));

    public static async Task<IResult> SummaryAsync(
        DateTimeOffset? from,
        DateTimeOffset? to,
        ILoggingService service,
        CancellationToken cancellationToken) =>
        await TranslateAsync(async () => Results.Ok(await service.GetLevelCountsAsync(from, to, cancellationToken)));

    public static async Task<IResult> ErrorsAsync(
        int? limit,
        int? offset,
        ILoggingService service,
        CancellationToken cancellationToken) =>
        await TranslateAsync(async () =>
        {
            var query = new LogQuery(
                MinimumLevel: LogLevel.Error,
                Limit: limit ?? 100,
                Offset: offset ?? 0);
            return Results.Ok(await service.QueryAsync(query, cancellationToken));
        });

    public static async Task<IResult> DeleteAsync(
        int? olderThanDays,
        ILoggingService service,
        CancellationToken cancellationToken) =>
        await TranslateAsync(async () =>
        {
            var deleted = await service.DeleteAsync(olderThanDays, cancellationToken);
            return Results.Ok(new LogDeleteResult(deleted));
        });

    private static LogLevel? ParseLevel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (Enum.TryParse<LogLevel>(value, ignoreCase: true, out var level) && level != LogLevel.None)
        {
            return level;
        }

        throw new InvalidLogQueryException($"Unknown log level '{value}'.");
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static async Task<IResult> TranslateAsync(Func<Task<IResult>> operation)
    {
        try
        {
            return await operation();
        }
        catch (InvalidLogQueryException exception)
        {
            throw new HttpException(StatusCodes.Status400BadRequest, "Invalid log query", exception.Message);
        }
        catch (LogStoreUnavailableException)
        {
            throw new HttpException(
                StatusCodes.Status503ServiceUnavailable,
                "Log store unavailable",
                "The log store is temporarily unavailable.");
        }
    }
}
