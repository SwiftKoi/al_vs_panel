namespace AlegacyWebPanel.Modules.FileManager.Configuration;

public sealed class FileManagerOptions
{
    public const string SectionName = "FileManager";

    public long MaximumFileSizeBytes { get; set; } = 10485760; // 10 MB
    public long MaximumTextFileSizeBytes { get; set; } = 1048576; // 1 MB
    // Largest total uncompressed size an extracted archive may expand to (zip-bomb guard).
    public long MaximumArchiveSizeBytes { get; set; } = 2147483648; // 2 GB
    public int MaximumArchiveEntries { get; set; } = 20000;
    public int MaximumListingEntries { get; set; } = 2000;
    public int MaximumSearchResults { get; set; } = 500;
    public int MaximumConcurrentOperations { get; set; } = 2;
    public int OperationTimeoutMinutes { get; set; } = 30;
    // Deleted items stay restorable in <root>/.trash this long, then are purged.
    public int TrashRetentionDays { get; set; } = 7;

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

    /// <summary>
    /// Root-relative paths the live game server uses (world saves, mods, configs). The panel
    /// asks for explicit confirmation before changing them while the server is online.
    /// </summary>
    public List<string> ProtectedPaths { get; set; } = [];
}
