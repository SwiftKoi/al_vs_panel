namespace AlegacyWebPanel.Core.Auditing;

/// <summary>Stable category names for audit entries, so the UI can filter and translate them.</summary>
public static class AuditCategories
{
    public const string Server = "server";
    public const string Moderation = "moderation";
    public const string Files = "files";
    public const string Mods = "mods";
    public const string Users = "users";
    public const string Account = "account";
    public const string Logs = "logs";
    public const string Remote = "remote";
}

/// <summary>One recorded change or privileged action. The trail adds the timestamp.</summary>
/// <param name="Actor">Username of the signed-in user, or <c>automation-api</c> for API-key calls.</param>
/// <param name="ActorRole"><c>Admin</c>, <c>Moderator</c> or <c>api</c>.</param>
/// <param name="Category">One of <see cref="AuditCategories"/>.</param>
/// <param name="Action">Short verb within the category, for example <c>upload</c> or <c>restart</c>.</param>
/// <param name="ServerId">The game server the action targeted, when there is one.</param>
/// <param name="Target">The main object of the action: a file path, a player, a mod id, a user id.</param>
/// <param name="DetailsJson">A JSON object with the remaining request fields, secrets removed.</param>
public sealed record AuditEvent(
    string Actor,
    string ActorRole,
    string? IpAddress,
    string Category,
    string Action,
    string? ServerId,
    string? Target,
    string? DetailsJson,
    bool Succeeded,
    string? Error);

/// <summary>
/// Append-only record of who did what. Implementations must never throw into the caller:
/// an audit storage failure must not stop the action being audited.
/// </summary>
public interface IAuditTrail
{
    Task RecordAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default);
}
