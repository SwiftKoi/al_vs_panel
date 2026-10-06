using AlegacyWebPanel.Modules.Audit.Contracts;

namespace AlegacyWebPanel.Modules.Audit.Services;

public interface IAuditQueryService
{
    Task<AuditPageResult> QueryAsync(AuditQuery query, CancellationToken cancellationToken);

    Task<AuditFacets> GetFacetsAsync(CancellationToken cancellationToken);
}
