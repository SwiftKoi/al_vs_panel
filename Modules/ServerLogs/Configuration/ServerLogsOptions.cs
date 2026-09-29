namespace AlegacyWebPanel.Modules.ServerLogs.Configuration;

public sealed class ServerLogsOptions
{
    public const string SectionName = "ServerLogs";

    public bool Enabled { get; set; } = true;
    public string DatabasePath { get; set; } = "/var/lib/alegacy/data/serverlogs.db";
    public int IndexIntervalSeconds { get; set; } = 30;

    // Bytes read from the game server per file request and per indexing pass. The pass limit
    // spreads the first backfill of the archives over several passes.
    public int ReadChunkBytes { get; set; } = 1024 * 1024;
    public long MaximumBytesPerPass { get; set; } = 8L * 1024 * 1024;

    // Days to keep entries, per log kind (main, audit, debug, chat).
    public Dictionary<string, int> RetentionDays { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["main"] = 180,
        ["audit"] = 30,
        ["debug"] = 14,
        ["chat"] = 30
    };

    public int MaximumPageSize { get; set; } = 500;
    public int MaximumContextLines { get; set; } = 200;
    public int MaximumExportRows { get; set; } = 100_000;

    // Per-server log sources, keyed by ServerManagement server ID.
    public Dictionary<string, ServerLogsServerOptions> Servers { get; set; } = [];
}

public sealed class ServerLogsServerOptions
{
    // RemoteOperations name that runs Server/*/server-logs.py against the server's Data directory.
    public string Operation { get; set; } = string.Empty;

    // Chat logs are players' conversations; they are indexed only when enabled.
    public bool IncludeChat { get; set; }
}
