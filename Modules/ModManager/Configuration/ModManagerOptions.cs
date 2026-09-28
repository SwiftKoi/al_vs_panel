namespace AlegacyWebPanel.Modules.ModManager.Configuration;

public sealed class ModManagerOptions
{
    public const string SectionName = "ModManager";

    public string ModDbBaseUrl { get; set; } = "https://mods.vintagestory.at";

    // Hosts a mod download may come from, checked on the final URI after redirects.
    public List<string> TrustedDownloadHosts { get; set; } = ["mods.vintagestory.at", "moddbcdn.vintagestory.at"];

    public int CacheMinutes { get; set; } = 30;
    public int RequestTimeoutSeconds { get; set; } = 30;
    public long MaximumApiResponseBytes { get; set; } = 4 * 1024 * 1024;
    public long MaximumDownloadBytes { get; set; } = 300L * 1024 * 1024;
    public int MaximumConcurrentRequests { get; set; } = 4;

    // Per-server mod sources, keyed by ServerManagement server ID.
    public Dictionary<string, ModManagerServerOptions> Servers { get; set; } = [];
}

public sealed class ModManagerServerOptions
{
    // RemoteOperations name that runs Server/*/mod-manager.py against the server's Data directory.
    public string Operation { get; set; } = string.Empty;

    // Overrides the game version read from the server log.
    public string? GameVersion { get; set; }
}
