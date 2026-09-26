using AlegacyWebPanel.Core.Abstractions;
using AlegacyWebPanel.Core.Errors;
using AlegacyWebPanel.Core.Security;
using AlegacyWebPanel.Modules.AutomationApi.Configuration;
using AlegacyWebPanel.Modules.AutomationApi.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AlegacyWebPanel.AutomationApi.UnitTests;

public sealed class ApiKeyValidatorTests
{
    private const string ConfiguredKey = "correct-horse-battery-staple";

    [Fact]
    public void IsValid_accepts_the_configured_key()
    {
        var validator = CreateValidator(ConfiguredKey);

        Assert.True(validator.IsValid(ConfiguredKey));
    }

    [Theory]
    [InlineData("wrong-key")]
    [InlineData("correct-horse-battery-staple-plus")]
    [InlineData("")]
    [InlineData(null)]
    public void IsValid_rejects_any_other_key(string? presented)
    {
        var validator = CreateValidator(ConfiguredKey);

        Assert.False(validator.IsValid(presented));
    }

    [Fact]
    public void IsValid_returns_false_when_the_api_is_disabled()
    {
        var validator = CreateValidator(ConfiguredKey, enabled: false);

        Assert.False(validator.IsValid(ConfiguredKey));
    }

    [Fact]
    public void IsValid_returns_false_when_the_secret_is_unavailable()
    {
        var validator = new ApiKeyValidator(
            new FakeSecretReader(null),
            Options.Create(new AutomationApiOptions { Enabled = true, KeyFile = "/run/secrets/api_key" }),
            NullLogger<ApiKeyValidator>.Instance);

        Assert.False(validator.IsValid(ConfiguredKey));
    }

    private static ApiKeyValidator CreateValidator(string? secret, bool enabled = true) =>
        new(
            new FakeSecretReader(secret),
            Options.Create(new AutomationApiOptions { Enabled = enabled, KeyFile = "/run/secrets/api_key" }),
            NullLogger<ApiKeyValidator>.Instance);

    private sealed class FakeSecretReader(string? value) : ISecretReader
    {
        public SecretValue ReadRequired(string path, string name) =>
            value is null
                ? throw new ConfigurationException($"Required secret '{name}' was not found at '{path}'.")
                : new SecretValue(value);
    }
}
