using AlegacyWebPanel.Core.Security;

namespace AlegacyWebPanel.Core.Abstractions;

/// <summary>
/// Reads secret values from a secure storage location.
/// </summary>
public interface ISecretReader
{
    /// <summary>
    /// Reads a required secret value from the specified path.
    /// </summary>
    /// <param name="path">The file path or storage location of the secret.</param>
    /// <param name="name">The name identifying the secret to retrieve.</param>
    ///
    /// <returns>A <see cref="SecretValue"/> containing the secret.</returns>
    ///
    /// <exception cref="ConfigurationException">Thrown when the secret is not found or is empty.</exception>
    SecretValue ReadRequired(string path, string name);
}
