namespace AlegacyWebPanel.Modules.AutomationApi.Services;

public interface IApiKeyValidator
{
    bool IsValid(string? presentedKey);
}
