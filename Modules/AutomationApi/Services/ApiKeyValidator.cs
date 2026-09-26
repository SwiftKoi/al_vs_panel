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
    ILogger<ApiKeyValidator> logger) : IApiKeyValidator
{
    private int _unavailableLogged;

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

        string expected;
        try
        {
            // Read per request so replacing the mounted secret rotates the key
            // without a restart.
            expected = secretReader.ReadRequired(settings.KeyFile, "automation API key").Value;
        }
        catch (ConfigurationException exception)
        {
            if (Interlocked.Exchange(ref _unavailableLogged, 1) == 0)
            {
                logger.LogWarning(exception, "The automation API key is unavailable. API requests are rejected.");
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
