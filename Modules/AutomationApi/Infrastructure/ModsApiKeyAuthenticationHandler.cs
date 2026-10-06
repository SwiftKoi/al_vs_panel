using System.Text.Encodings.Web;
using AlegacyWebPanel.Modules.AutomationApi.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace AlegacyWebPanel.Modules.AutomationApi.Infrastructure;

/// <summary>Authenticates the read-only mods catalog key. It shares nothing with the automation key.</summary>
public sealed class ModsApiKeyAuthenticationHandler(
    IOptionsMonitor<ApiKeyAuthenticationOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    [FromKeyedServices(ApiKeyScope.Mods)] IApiKeyValidator validator)
    : ApiKeyAuthenticationHandler(options, loggerFactory, encoder, validator)
{
    protected override string SchemeName => ApiKeyAuthenticationDefaults.ModsScheme;

    protected override string IdentityName => "mods-api";
}
