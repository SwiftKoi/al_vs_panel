namespace AlegacyWebPanel.Modules.Logging.Configuration;

public sealed class LogStoreOptions
{
    public const string SectionName = "LogStore";

    public string DatabasePath { get; set; } = "/var/lib/alegacy/data/logs.db";

    public string MinimumLevel { get; set; } = "Information";

    public int MaximumRetainedDays { get; set; } = 30;

    public long MaximumRetainedEntries { get; set; } = 500_000;

    public int BufferCapacity { get; set; } = 4096;

    public int FlushBatchSize { get; set; } = 100;

    public int PruneIntervalMinutes { get; set; } = 60;

    public int StructuredStateMaxBytes { get; set; } = 4096;
}
