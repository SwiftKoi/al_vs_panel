using System.Security.Cryptography;
using System.Text;
using AlegacyWebPanel.Core.Abstractions;
using AlegacyWebPanel.Core.Errors;
using AlegacyWebPanel.Modules.AutomationApi.Configuration;
using Microsoft.Extensions.Options;

namespace AlegacyWebPanel.Modules.AutomationApi.Services;

public sealed class ApiKeyValidator(
    ISecretReader secretReader,
    IOptions<AutomationApiOptions> options,
    ILogger<ApiKeyValidator> logger,
    ApiKeyScope scope) : IApiKeyValidator
{
    private int _unavailableLogged;

    public ApiKeyValidator(
        ISecretReader secretReader,
        IOptions<AutomationApiOptions> options,
        ILogger<ApiKeyValidator> logger)
        : this(secretReader, options, logger, ApiKeyScope.Automation)
    {
    }

    public bool IsValid(string? presentedKey)
    {
        if (string.IsNullOrEmpty(presentedKey))
        {
            return false;
        }

        var settings = options.Value;
        if (!settings.Enabled)
        {
            return false;
        }

        var keyFile = scope == ApiKeyScope.Mods ? settings.ModsKeyFile : settings.KeyFile;
        if (string.IsNullOrWhiteSpace(keyFile))
        {
            return false;
        }

        string expected;
        try
        {
            // Read per request so replacing the mounted secret rotates the key
            // without a restart.
            expected = secretReader.ReadRequired(keyFile, scope == ApiKeyScope.Mods ? "mods API key" : "automation API key").Value;
        }
        catch (ConfigurationException exception)
        {
            if (Interlocked.Exchange(ref _unavailableLogged, 1) == 0)
            {
                logger.LogWarning(exception, "The {Scope} API key is unavailable. API requests are rejected.", scope);
            }

            return false;
        }

        // Hash both values first so the comparison neither leaks length nor
        // depends on an early-exit character comparison.
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        var presentedHash = SHA256.HashData(Encoding.UTF8.GetBytes(presentedKey));
        return CryptographicOperations.FixedTimeEquals(expectedHash, presentedHash);
    }
}
