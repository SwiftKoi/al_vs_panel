namespace AlegacyWebPanel.Modules.Audit.Contracts;

public sealed record AuditQuery(
    string? Actor = null,
    string? Category = null,
    string? Action = null,
    string? ServerId = null,
    bool? Succeeded = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    string? Search = null,
    int Limit = 100,
    int Offset = 0);

public sealed record AuditEntryDto(
    long Id,
    DateTimeOffset TimestampUtc,
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

public sealed record AuditQueryResult(IReadOnlyList<AuditEntryDto> Items, long Total);

public sealed record AuditPageResult(
    IReadOnlyList<AuditEntryDto> Items,
    long Total,
    int Offset,
    int Limit);

public sealed record AuditActionFacet(string Category, string Action);

/// <summary>The values that occur in the trail, for the viewer's filter drop-downs.</summary>
public sealed record AuditFacets(
    IReadOnlyList<string> Actors,
    IReadOnlyList<AuditActionFacet> Actions,
    IReadOnlyList<string> Servers);
