using AlegacyWebPanel.Core.Errors;
using AlegacyWebPanel.Modules.Analytics.Exceptions;
using AlegacyWebPanel.Modules.Analytics.Services;

namespace AlegacyWebPanel.Modules.Analytics.Endpoints;

public static class AnalyticsEndpoints
{
    public const int DefaultDays = 30;
    public const int DefaultDisconnectDays = 7;
    public const int DefaultHours = 24;
    public const int DefaultHeatmapDays = 28;

    public static Task<IResult> PlayerSummaryAsync(
        string serverId,
        int? days,
        IAnalyticsService service,
        CancellationToken cancellationToken) =>
        TranslateAsync(() => service.GetPlayerSummaryAsync(serverId, days ?? DefaultDays, cancellationToken));

    public static Task<IResult> DisconnectReportAsync(
        string serverId,
        int? days,
        IAnalyticsService service,
        CancellationToken cancellationToken) =>
        TranslateAsync(() => service.GetDisconnectReportAsync(serverId, days ?? DefaultDisconnectDays, cancellationToken));

    public static Task<IResult> ActivityHeatmapAsync(
        string serverId,
        int? days,
        IAnalyticsService service,
        CancellationToken cancellationToken) =>
        TranslateAsync(() => service.GetActivityHeatmapAsync(serverId, days ?? DefaultHeatmapDays, cancellationToken));

    public static Task<IResult> PlayerListAsync(
        string serverId,
        int? days,
        IAnalyticsService service,
        CancellationToken cancellationToken) =>
        TranslateAsync(() => service.GetPlayerListAsync(serverId, days ?? DefaultDays, cancellationToken));

    public static Task<IResult> PlayerProfileAsync(
        string serverId,
        string playerName,
        int? days,
        IAnalyticsService service,
        CancellationToken cancellationToken) =>
        TranslateAsync(() => service.GetPlayerProfileAsync(serverId, playerName, days ?? DefaultDays, cancellationToken));

    public static Task<IResult> ServerHealthAsync(
        string serverId,
        int? hours,
        IAnalyticsService service,
        CancellationToken cancellationToken) =>
        TranslateAsync(() => service.GetServerHealthAsync(serverId, hours ?? DefaultHours, cancellationToken));

    public static Task<IResult> ConnectionQualityAsync(
        string serverId,
        int? hours,
        IAnalyticsService service,
        CancellationToken cancellationToken) =>
        TranslateAsync(() => service.GetConnectionQualityAsync(serverId, hours ?? DefaultHours, cancellationToken));

    public static Task<IResult> PlayerConnectionHistoryAsync(
        string serverId,
        string playerName,
        int? hours,
        IAnalyticsService service,
        CancellationToken cancellationToken) =>
        TranslateAsync(() => service.GetPlayerConnectionHistoryAsync(
            serverId, playerName, hours ?? DefaultHours, cancellationToken));

    private static async Task<IResult> TranslateAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return Results.Ok(await operation());
        }
        catch (AnalyticsServerNotFoundException exception)
        {
            throw new HttpException(StatusCodes.Status404NotFound, "Server not found", exception.Message);
        }
        catch (AnalyticsPlayerNotFoundException exception)
        {
            throw new HttpException(StatusCodes.Status404NotFound, "Player not found", exception.Message);
        }
        catch (InvalidAnalyticsRangeException exception)
        {
            throw new HttpException(StatusCodes.Status400BadRequest, "Invalid analytics range", exception.Message);
        }
    }
}
