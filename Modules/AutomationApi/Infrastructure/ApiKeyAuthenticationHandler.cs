using System.Security.Claims;
using System.Text.Encodings.Web;
using AlegacyWebPanel.Modules.AutomationApi.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace AlegacyWebPanel.Modules.AutomationApi.Infrastructure;

public class ApiKeyAuthenticationHandler(
    IOptionsMonitor<ApiKeyAuthenticationOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    IApiKeyValidator validator)
    : AuthenticationHandler<ApiKeyAuthenticationOptions>(options, loggerFactory, encoder)
{
    protected virtual string SchemeName => ApiKeyAuthenticationDefaults.Scheme;

    protected virtual string IdentityName => "automation-api";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiKeyAuthenticationDefaults.HeaderName, out var headerValues))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        // StringValues joins repeated headers, so a duplicated header never
        // matches a single configured key and fails closed.
        if (!validator.IsValid(headerValues.ToString()))
        {
            return Task.FromResult(AuthenticateResult.Fail("A valid API key is required."));
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, IdentityName)],
            SchemeName);
        var ticket = new AuthenticationTicket(
            new ClaimsPrincipal(identity),
            SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
