using AlegacyWebPanel.Core.Errors;
using AlegacyWebPanel.Modules.Audit.Contracts;
using AlegacyWebPanel.Modules.Audit.Exceptions;
using AlegacyWebPanel.Modules.Audit.Services;

namespace AlegacyWebPanel.Modules.Audit.Endpoints;

public static class AuditEndpoints
{
    public static async Task<IResult> QueryAsync(
        string? actor,
        string? category,
        string? action,
        string? server,
        bool? succeeded,
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? search,
        int? limit,
        int? offset,
        IAuditQueryService service,
        CancellationToken cancellationToken) =>
        await TranslateAsync(async () =>
        {
            var query = new AuditQuery(
                Normalize(actor),
                Normalize(category),
                Normalize(action),
                Normalize(server),
                succeeded,
                from,
                to,
                Normalize(search),
                limit ?? 100,
                offset ?? 0);
            return Results.Ok(await service.QueryAsync(query, cancellationToken));
        });

    public static async Task<IResult> FacetsAsync(
        IAuditQueryService service,
        CancellationToken cancellationToken) =>
        await TranslateAsync(async () => Results.Ok(await service.GetFacetsAsync(cancellationToken)));

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static async Task<IResult> TranslateAsync(Func<Task<IResult>> operation)
    {
        try
        {
            return await operation();
        }
        catch (InvalidAuditQueryException exception)
        {
            throw new HttpException(StatusCodes.Status400BadRequest, "Invalid audit query", exception.Message);
        }
        catch (AuditStoreUnavailableException)
        {
            throw new HttpException(
                StatusCodes.Status503ServiceUnavailable,
                "Audit store unavailable",
                "The audit store is temporarily unavailable.");
        }
    }
}
