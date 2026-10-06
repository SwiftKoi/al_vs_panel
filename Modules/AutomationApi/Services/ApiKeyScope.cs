namespace AlegacyWebPanel.Modules.AutomationApi.Services;

/// <summary>Which pre-shared key a validator checks. Each scope has its own secret file.</summary>
public enum ApiKeyScope
{
    /// <summary>The full automation key: file transfers and server lifecycle.</summary>
    Automation,

    /// <summary>The read-only mods catalog key.</summary>
    Mods
}
