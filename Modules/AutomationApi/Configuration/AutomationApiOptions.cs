namespace AlegacyWebPanel.Modules.AutomationApi.Configuration;

public sealed class AutomationApiOptions
{
    public const string SectionName = "AutomationApi";

    public bool Enabled { get; set; }

    public string KeyFile { get; set; } = "/run/secrets/api_key";
}
