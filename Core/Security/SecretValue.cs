namespace AlegacyWebPanel.Core.Security;

public sealed class SecretValue
{
    public SecretValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A secret value cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => "[REDACTED]";
}
