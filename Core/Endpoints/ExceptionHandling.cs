using AlegacyWebPanel.Core.Errors;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AlegacyWebPanel.Core.Endpoints;

public static class ExceptionHandling
{
    public static IApplicationBuilder UseCoreExceptionHandling(this IApplicationBuilder app)
    {
        var logger = app.ApplicationServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("AlegacyWebPanel.Core.ExceptionHandling");

        return app.Use(async (context, next) =>
        {
            try
            {
                await next(context);
            }
            catch (HttpException exception)
            {
                logger.LogWarning(
                    exception,
                    "Request {Path} failed with status {StatusCode}",
                    context.Request.Path,
                    exception.StatusCode);
                context.Response.StatusCode = exception.StatusCode;
                await Results.Problem(
                    statusCode: exception.StatusCode,
                    title: exception.Title,
                    detail: exception.Message).ExecuteAsync(context);
            }
            catch (DomainException exception)
            {
                logger.LogError(
                    exception,
                    "Request {Path} failed with an unhandled domain error",
                    context.Request.Path);
                await Results.Problem(statusCode: StatusCodes.Status500InternalServerError).ExecuteAsync(context);
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Request {Path} threw an unhandled exception",
                    context.Request.Path);
                throw;
            }
        });
    }
}
