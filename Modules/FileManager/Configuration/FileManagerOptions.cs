namespace AlegacyWebPanel.Modules.FileManager.Configuration;

public sealed class FileManagerOptions
{
    public const string SectionName = "FileManager";

    public long MaximumFileSizeBytes { get; set; } = 10485760; // 10 MB
    public long MaximumTextFileSizeBytes { get; set; } = 1048576; // 1 MB
    public long MaximumArchiveSizeBytes { get; set; } = 52428800; // 50 MB
    public int MaximumListingEntries { get; set; } = 2000;
    public int MaximumConcurrentOperations { get; set; } = 2;

    public Dictionary<string, ServerInstanceRoots> Instances { get; set; } = [];
}

public sealed class ServerInstanceRoots
{
    public Dictionary<string, FileRootConfig> Roots { get; set; } = [];
}

public sealed class FileRootConfig
{
    public string DisplayName { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public bool IsWritable { get; set; }
}
