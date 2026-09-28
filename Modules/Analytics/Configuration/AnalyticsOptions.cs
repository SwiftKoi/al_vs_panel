namespace AlegacyWebPanel.Modules.Analytics.Configuration;

public sealed class AnalyticsOptions
{
    public const string SectionName = "Analytics";

    public bool Enabled { get; set; } = true;
    public string DatabasePath { get; set; } = "/var/lib/alegacy/data/analytics.db";
    public int SampleIntervalSeconds { get; set; } = 60;
    public int EventImportIntervalMinutes { get; set; } = 5;
    public int SampleRetentionDays { get; set; } = 90;
    public int EventRetentionDays { get; set; } = 400;

    // IANA time zone used to bucket statistics into calendar days.
    public string TimeZone { get; set; } = "UTC";

    // Client addresses (single IPs or CIDR ranges) that belong to a connection proxy.
    public List<string> ProxyAddresses { get; set; } = [];

    // Per-server analytics sources, keyed by ServerManagement server ID.
    public Dictionary<string, AnalyticsServerOptions> Servers { get; set; } = [];
}

public sealed class AnalyticsServerOptions
{
    // RemoteOperations name that prints typed, tab-separated player and server
    // events (see Server/Production/server-player-events.sh for the format).
    public string PlayerEventsOperation { get; set; } = string.Empty;
}
