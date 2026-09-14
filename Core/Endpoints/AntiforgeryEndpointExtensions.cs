using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace AlegacyWebPanel.Core.Endpoints;

public static class AntiforgeryEndpointExtensions
{
    public static RouteHandlerBuilder RequireAntiforgery(this RouteHandlerBuilder builder)
    {
        return builder.WithMetadata(new RequireAntiforgeryTokenAttribute());
    }
}
