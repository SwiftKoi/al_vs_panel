namespace AlegacyWebPanel.Modules.AutomationApi.Infrastructure;

public static class ApiKeyAuthenticationDefaults
{
    public const string Scheme = "AutomationApiKey";
    public const string Policy = "AutomationApi";
    public const string HeaderName = "X-Api-Key";

    public const string ModsScheme = "AutomationModsApiKey";
    public const string ModsPolicy = "AutomationApiMods";
}
