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

    [Fact]
    public void Mods_scope_accepts_the_mods_key_and_ignores_the_automation_key()
    {
        var reader = new PathSecretReader(new Dictionary<string, string>
        {
            ["/run/secrets/api_key"] = "automation-key",
            ["/run/secrets/mods_api_key"] = "mods-key"
        });
        var options = Options.Create(new AutomationApiOptions
        {
            Enabled = true,
            KeyFile = "/run/secrets/api_key",
            ModsKeyFile = "/run/secrets/mods_api_key"
        });
        var automation = new ApiKeyValidator(reader, options, NullLogger<ApiKeyValidator>.Instance, ApiKeyScope.Automation);
        var mods = new ApiKeyValidator(reader, options, NullLogger<ApiKeyValidator>.Instance, ApiKeyScope.Mods);

        Assert.True(mods.IsValid("mods-key"));
        Assert.False(mods.IsValid("automation-key"));
        Assert.True(automation.IsValid("automation-key"));
        Assert.False(automation.IsValid("mods-key"));
    }

    [Fact]
    public void Mods_scope_rejects_everything_when_no_mods_key_file_is_configured()
    {
        var validator = new ApiKeyValidator(
            new FakeSecretReader(ConfiguredKey),
            Options.Create(new AutomationApiOptions { Enabled = true, KeyFile = "/run/secrets/api_key", ModsKeyFile = "" }),
            NullLogger<ApiKeyValidator>.Instance,
            ApiKeyScope.Mods);

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

    private sealed class PathSecretReader(IReadOnlyDictionary<string, string> secrets) : ISecretReader
    {
        public SecretValue ReadRequired(string path, string name) =>
            secrets.TryGetValue(path, out var value)
                ? new SecretValue(value)
                : throw new ConfigurationException($"Required secret '{name}' was not found at '{path}'.");
    }
}
