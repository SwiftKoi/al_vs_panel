using AlegacyWebPanel.Core.Abstractions;
using AlegacyWebPanel.Core.Errors;
using AlegacyWebPanel.Core.Security;

namespace AlegacyWebPanel.Core.Infrastructure.Secrets;

public sealed class SecretFileReader : ISecretReader
{
    public SecretValue ReadRequired(string path, string name)
    {
        if (!File.Exists(path))
        {
            throw new ConfigurationException($"Required secret '{name}' was not found at '{path}'.");
        }

        var value = File.ReadAllText(path).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ConfigurationException($"Required secret '{name}' is empty.");
        }

        return new SecretValue(value);
    }
}
