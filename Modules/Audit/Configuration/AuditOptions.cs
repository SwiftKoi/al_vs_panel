namespace AlegacyWebPanel.Modules.Audit.Configuration;

public sealed class AuditOptions
{
    public const string SectionName = "AuditTrail";

    public string DatabasePath { get; set; } = "/var/lib/alegacy/data/audit.db";

    public int RetentionDays { get; set; } = 365;

    public int PruneIntervalMinutes { get; set; } = 360;

    public int MaximumPageSize { get; set; } = 500;
}
