using AlegacyWebPanel.Core.Errors;
using AlegacyWebPanel.Modules.RemoteOperations.Exceptions;
using AlegacyWebPanel.Modules.RemoteOperations.Services;
using Microsoft.AspNetCore.Http;

namespace AlegacyWebPanel.Modules.RemoteOperations.Endpoints;

public static class RemoteOperationsEndpoints
{
    public static async Task<IResult> ExecuteAsync(
        string operation,
        IRemoteOperationsService service,
        CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(await service.ExecuteAsync(operation, cancellationToken));
        }
        catch (RemoteOperationNotAllowedException exception)
        {
            throw new HttpException(StatusCodes.Status404NotFound, "Operation not found", exception.Message);
        }
        catch (RemoteConfigurationException exception)
        {
            throw new HttpException(StatusCodes.Status503ServiceUnavailable, "Remote service unavailable", exception.Message);
        }
    }
}
