namespace AlegacyWebPanel.Core.Errors;

public sealed class ConfigurationException(string message) : InvalidOperationException(message);
