using AlegacyWebPanel.Modules.Audit.Configuration;
using AlegacyWebPanel.Modules.Audit.Contracts;
using AlegacyWebPanel.Modules.Audit.Exceptions;
using AlegacyWebPanel.Modules.Audit.Persistence;
using Microsoft.Extensions.Options;

namespace AlegacyWebPanel.Modules.Audit.Services;

public sealed class AuditQueryService(IAuditRepository repository, IOptions<AuditOptions> options) : IAuditQueryService
{
    private const int MaximumFilterLength = 200;

    public async Task<AuditPageResult> QueryAsync(AuditQuery query, CancellationToken cancellationToken)
    {
        Validate(query);
        var result = await repository.QueryAsync(query, cancellationToken);
        return new AuditPageResult(result.Items, result.Total, query.Offset, query.Limit);
    }

    public Task<AuditFacets> GetFacetsAsync(CancellationToken cancellationToken) =>
        repository.GetFacetsAsync(cancellationToken);

    private void Validate(AuditQuery query)
    {
        var maximumPageSize = options.Value.MaximumPageSize;
        if (query.Limit < 1 || query.Limit > maximumPageSize)
        {
            throw new InvalidAuditQueryException($"The page size must be between 1 and {maximumPageSize}.");
        }

        if (query.Offset < 0)
        {
            throw new InvalidAuditQueryException("The offset must not be negative.");
        }

        if (query.From is { } from && query.To is { } to && from > to)
        {
            throw new InvalidAuditQueryException("The 'from' timestamp must not be after 'to'.");
        }

        foreach (var (name, value) in new[]
                 {
                     ("actor", query.Actor), ("category", query.Category), ("action", query.Action),
                     ("server", query.ServerId), ("search", query.Search)
                 })
        {
            if (value is { Length: > MaximumFilterLength })
            {
                throw new InvalidAuditQueryException($"The {name} filter must be at most {MaximumFilterLength} characters.");
            }
        }
    }
}
