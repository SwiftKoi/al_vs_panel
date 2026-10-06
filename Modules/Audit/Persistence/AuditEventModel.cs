namespace AlegacyWebPanel.Modules.Audit.Persistence;

public sealed class AuditEventModel
{
    public long Id { get; set; }

    public DateTime TimestampUtc { get; set; }

    public string Actor { get; set; } = string.Empty;

    public string ActorRole { get; set; } = string.Empty;

    public string? IpAddress { get; set; }

    public string Category { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;

    public string? ServerId { get; set; }

    public string? Target { get; set; }

    public string? DetailsJson { get; set; }

    public bool Succeeded { get; set; }

    public string? Error { get; set; }
}
