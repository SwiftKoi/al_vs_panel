using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;

namespace AlegacyWebPanel.Core.Auditing;

public static class AuditEndpointExtensions
{
    /// <summary>
    /// Records every call of this endpoint in the audit trail: who, from where, what, and whether it
    /// worked. Authorization runs first, so only requests the caller was allowed to make are recorded.
    /// Failed attempts (exceptions and 4xx/5xx results) are recorded with the reason. If no
    /// <see cref="IAuditTrail"/> is registered the endpoint behaves as if it were not audited.
    /// </summary>
    public static TBuilder Audited<TBuilder>(this TBuilder builder, string category, string action)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            try
            {
                var result = await next(context);
                var (succeeded, error) = Outcome(result);
                await RecordAsync(context, category, action, succeeded, error);
                return result;
            }
            catch (Exception exception)
            {
                var error = exception is OperationCanceledException ? "Canceled" : exception.Message;
                await RecordAsync(context, category, action, succeeded: false, error);
                throw;
            }
        });

    private static (bool Succeeded, string? Error) Outcome(object? result)
    {
        if (result is ProblemHttpResult problem)
        {
            return (false, problem.ProblemDetails.Detail ?? problem.ProblemDetails.Title);
        }

        if (result is IStatusCodeHttpResult { StatusCode: >= 400 } status)
        {
            return (false, ((int)status.StatusCode!).ToString());
        }

        return (true, null);
    }

    private static async ValueTask RecordAsync(
        EndpointFilterInvocationContext context,
        string category,
        string action,
        bool succeeded,
        string? error)
    {
        try
        {
            var http = context.HttpContext;
            var trail = http.RequestServices.GetService<IAuditTrail>();
            if (trail is null)
            {
                return;
            }

            var description = AuditRequestDescriber.Describe(http, context.Arguments);
            var role = http.User.FindFirst(ClaimTypes.Role)?.Value ?? "api";
            await trail.RecordAsync(
                new AuditEvent(
                    http.User.Identity?.Name ?? "unknown",
                    role,
                    http.Connection.RemoteIpAddress?.ToString(),
                    category,
                    action,
                    description.ServerId,
                    description.Target,
                    description.DetailsJson,
                    succeeded,
                    error is { Length: > AuditRequestDescriber.MaximumValueLength }
                        ? error[..AuditRequestDescriber.MaximumValueLength] + "…"
                        : error),
                CancellationToken.None);
        }
        catch
        {
            // Auditing must never turn a successful action into a failure.
        }
    }
}
