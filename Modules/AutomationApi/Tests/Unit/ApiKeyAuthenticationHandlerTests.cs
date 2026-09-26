using System.Text.Encodings.Web;
using AlegacyWebPanel.Modules.AutomationApi.Infrastructure;
using AlegacyWebPanel.Modules.AutomationApi.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AlegacyWebPanel.AutomationApi.UnitTests;

public sealed class ApiKeyAuthenticationHandlerTests
{
    [Fact]
    public async Task Authenticate_without_header_returns_no_result()
    {
        var handler = CreateHandler(new FakeValidator { IsValid = false });
        await handler.InitializeAsync(Scheme, new DefaultHttpContext());

        var result = await handler.AuthenticateAsync();

        Assert.True(result.None);
    }

    [Fact]
    public async Task Authenticate_with_valid_key_returns_authenticated_principal()
    {
        var validator = new FakeValidator { IsValid = true };
        var handler = CreateHandler(validator);

        var context = new DefaultHttpContext();
        context.Request.Headers[ApiKeyAuthenticationDefaults.HeaderName] = "secret-value";
        await handler.InitializeAsync(Scheme, context);

        var result = await handler.AuthenticateAsync();

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Principal);
        Assert.Equal("secret-value", validator.PresentedKey);
    }

    [Fact]
    public async Task Authenticate_with_invalid_key_fails()
    {
        var handler = CreateHandler(new FakeValidator { IsValid = false });

        var context = new DefaultHttpContext();
        context.Request.Headers[ApiKeyAuthenticationDefaults.HeaderName] = "wrong-value";
        await handler.InitializeAsync(Scheme, context);

        var result = await handler.AuthenticateAsync();

        Assert.False(result.Succeeded);
    }

    private static AuthenticationScheme Scheme =>
        new(ApiKeyAuthenticationDefaults.Scheme, null, typeof(ApiKeyAuthenticationHandler));

    private static ApiKeyAuthenticationHandler CreateHandler(IApiKeyValidator validator) =>
        new(
            new TestOptionsMonitor<ApiKeyAuthenticationOptions>(new ApiKeyAuthenticationOptions()),
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            validator);

    private sealed class FakeValidator : IApiKeyValidator
    {
        public bool IsValid { get; init; }

        public string? PresentedKey { get; private set; }

        bool IApiKeyValidator.IsValid(string? presentedKey)
        {
            PresentedKey = presentedKey;
            return IsValid;
        }
    }

    private sealed class TestOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = value;

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
