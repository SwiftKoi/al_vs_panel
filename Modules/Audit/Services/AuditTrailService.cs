using AlegacyWebPanel.Core.Auditing;
using AlegacyWebPanel.Modules.Audit.Persistence;
using Microsoft.Extensions.Logging;

namespace AlegacyWebPanel.Modules.Audit.Services;

/// <summary>Writes audit events. A storage failure is logged and swallowed so it can never break the audited action.</summary>
public sealed class AuditTrailService(
    IAuditRepository repository,
    TimeProvider timeProvider,
    ILogger<AuditTrailService> logger) : IAuditTrail
{
    public async Task RecordAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        try
        {
            await repository.AddAsync(auditEvent, timeProvider.GetUtcNow(), cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Could not record audit event {Category}/{Action} by {Actor}",
                auditEvent.Category,
                auditEvent.Action,
                auditEvent.Actor);
        }
    }
}
