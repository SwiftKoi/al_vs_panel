namespace AlegacyWebPanel.Modules.AutomationApi.Configuration;

public sealed class AutomationApiOptions
{
    public const string SectionName = "AutomationApi";

    public bool Enabled { get; set; }

    public string KeyFile { get; set; } = "/run/secrets/api_key";

    /// <summary>
    /// Secret file for the read-only mods catalog (<c>GET /api/v1/servers/{id}/mods</c>). It is a different
    /// key from <see cref="KeyFile"/> so the public website can read the catalog without being able to
    /// upload files or control the game server. Empty means the catalog route is not mapped.
    /// </summary>
    public string ModsKeyFile { get; set; } = string.Empty;
}
