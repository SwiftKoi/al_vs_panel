using AlegacyWebPanel.Core.Security;

namespace AlegacyWebPanel.Core.Tests;

public sealed class SecretValueTests
{
    [Fact]
    public void ToString_does_not_expose_the_secret()
    {
        var secret = new SecretValue("not-for-logs");

        Assert.Equal("[REDACTED]", secret.ToString());
        Assert.Equal("not-for-logs", secret.Value);
    }

    [Fact]
    public void Empty_values_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => new SecretValue("  "));
    }
}
