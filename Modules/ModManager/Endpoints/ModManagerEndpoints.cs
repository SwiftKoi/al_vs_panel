using AlegacyWebPanel.Core.Errors;
using AlegacyWebPanel.Modules.ModManager.Contracts;
using AlegacyWebPanel.Modules.ModManager.Exceptions;
using AlegacyWebPanel.Modules.ModManager.Services;

namespace AlegacyWebPanel.Modules.ModManager.Endpoints;

public static class ModManagerEndpoints
{
    public static Task<IResult> OverviewAsync(
        string serverId,
        bool? refresh,
        IModManagerService service,
        CancellationToken cancellationToken) =>
        TranslateAsync(async () => Results.Ok(await service.GetOverviewAsync(serverId, refresh ?? false, cancellationToken)));

    public static Task<IResult> DetailAsync(
        string serverId,
        string modId,
        IModManagerService service,
        CancellationToken cancellationToken) =>
        TranslateAsync(async () => Results.Ok(await service.GetDetailAsync(serverId, modId, cancellationToken)));

    public static Task<IResult> SetPinnedAsync(
        string serverId,
        string modId,
        ModPinRequest request,
        IModManagerService service,
        CancellationToken cancellationToken) =>
        TranslateAsync(async () =>
        {
            await service.SetPinnedAsync(serverId, modId, request.Pinned, cancellationToken);
            return Results.NoContent();
        });

    public static Task<IResult> StartUpdateAsync(
        string serverId,
        ModUpdateRequest request,
        IModManagerService service,
        CancellationToken cancellationToken) =>
        TranslateAsync(async () => Results.Accepted(value: await service.StartUpdateAsync(serverId, request, cancellationToken)));

    public static Task<IResult> CurrentJob(string serverId, IModManagerService service) =>
        TranslateAsync(() => Task.FromResult(service.GetCurrentJob(serverId) is { } job ? Results.Ok(job) : Results.NoContent()));

    public static Task<IResult> RollbackAsync(
        string serverId,
        IModManagerService service,
        CancellationToken cancellationToken) =>
        TranslateAsync(async () => Results.Ok(await service.RollbackAsync(serverId, cancellationToken)));

    private static async Task<IResult> TranslateAsync(Func<Task<IResult>> operation)
    {
        try
        {
            return await operation();
        }
        catch (ModServerNotFoundException exception)
        {
            throw new HttpException(StatusCodes.Status404NotFound, "Server not found", exception.Message);
        }
        catch (ModNotInstalledException exception)
        {
            throw new HttpException(StatusCodes.Status404NotFound, "Mod not installed", exception.Message);
        }
        catch (ModReleaseNotFoundException exception)
        {
            throw new HttpException(StatusCodes.Status404NotFound, "Release not found", exception.Message);
        }
        catch (ModBackupNotFoundException exception)
        {
            throw new HttpException(StatusCodes.Status404NotFound, "Nothing to roll back", exception.Message);
        }
        catch (ModValidationException exception)
        {
            throw new HttpException(StatusCodes.Status400BadRequest, "Invalid mod update", exception.Message);
        }
        catch (ModUpdateConflictException exception)
        {
            throw new HttpException(StatusCodes.Status409Conflict, "Update in progress", exception.Message);
        }
        catch (ModDbUnavailableException exception)
        {
            throw new HttpException(StatusCodes.Status502BadGateway, "ModDB unavailable", exception.Message);
        }
        catch (ModTargetException exception)
        {
            throw new HttpException(StatusCodes.Status502BadGateway, "Mod folder unavailable", exception.Message);
        }
    }
}
