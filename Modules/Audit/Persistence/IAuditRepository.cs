using AlegacyWebPanel.Core.Auditing;
using AlegacyWebPanel.Modules.Audit.Contracts;

namespace AlegacyWebPanel.Modules.Audit.Persistence;

public interface IAuditRepository
{
    Task AddAsync(AuditEvent auditEvent, DateTimeOffset timestamp, CancellationToken cancellationToken);

    Task<AuditQueryResult> QueryAsync(AuditQuery query, CancellationToken cancellationToken);

    Task<AuditFacets> GetFacetsAsync(CancellationToken cancellationToken);

    Task<long> PruneAsync(DateTimeOffset olderThan, CancellationToken cancellationToken);
}
