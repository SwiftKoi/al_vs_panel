using System.IO;
using AlegacyWebPanel.Core.Errors;
using AlegacyWebPanel.Modules.FileManager.Configuration;
using AlegacyWebPanel.Modules.FileManager.Exceptions;
using AlegacyWebPanel.Modules.FileManager.Services;
using AlegacyWebPanel.Modules.RemoteOperations.Exceptions;
using AlegacyWebPanel.Modules.ServerManagement.Contracts;
using AlegacyWebPanel.Modules.ServerManagement.Exceptions;
using AlegacyWebPanel.Modules.ServerManagement.Services;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace AlegacyWebPanel.Modules.AutomationApi.Endpoints;

public static class AutomationApiEndpoints
{
    public static Task<IResult> ListServersAsync(
        IServerManagementService service,
        CancellationToken cancellationToken) =>
        TranslateAsync(async () => Results.Ok(await service.ListAsync(cancellationToken)));

    public static Task<IResult> ServerStatusAsync(
        string serverId,
        IServerManagementService service,
        CancellationToken cancellationToken) =>
        TranslateAsync(async () => Results.Ok(await service.GetStatusAsync(serverId, cancellationToken)));

    public static Task<IResult> LifecycleAsync(
        string serverId,
        ServerLifecycleAction action,
        IServerManagementService service,
        CancellationToken cancellationToken) =>
        TranslateAsync(async () => Results.Ok(await service.ExecuteLifecycleAsync(serverId, action, cancellationToken)));

    public static Task<IResult> ListFilesAsync(
        string serverId,
        string root,
        IFileManagerService service,
        CancellationToken cancellationToken,
        string? path = null) =>
        TranslateAsync(async () => Results.Ok(await service.GetDirectoryListingAsync(
            serverId,
            root,
            path ?? string.Empty,
            cancellationToken)));

    public static async Task<IResult> DownloadFileAsync(
        string serverId,
        string root,
        IFileManagerService service,
        CancellationToken cancellationToken,
        string? path = null)
    {
        if (string.IsNullOrEmpty(path))
        {
            return Results.BadRequest("Path is required.");
        }

        var normalizedPath = path.Replace('\\', '/');
        var lastSlash = normalizedPath.LastIndexOf('/');
        var parentPath = lastSlash == -1 ? string.Empty : normalizedPath[..lastSlash];
        var fileName = lastSlash == -1 ? normalizedPath : normalizedPath[(lastSlash + 1)..];

        return await TranslateAsync(async () =>
        {
            // Resolve the entry before streaming so missing files, unknown roots,
            // and remote listing failures become translated HTTP errors instead
            // of a mid-stream failure after the response has started.
            var listing = await service.GetDirectoryListingAsync(serverId, root, parentPath, cancellationToken);
            if (listing.Entries.All(entry => entry.IsFolder ||
                    !entry.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase)))
            {
                throw new FileNotFoundException($"File '{path}' was not found.");
            }

            return Results.Stream(
                async stream => await service.DownloadFileAsync(serverId, root, path, stream, cancellationToken),
                "application/octet-stream",
                fileDownloadName: fileName);
        });
    }

    public static async Task<IResult> UploadFileAsync(
        string serverId,
        string root,
        HttpRequest request,
        IFileManagerService service,
        IOptions<FileManagerOptions> options,
        CancellationToken cancellationToken,
        string? path = null)
    {
        if (request.ContentType is null ||
            !request.ContentType.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest("Request must be multipart/form-data");
        }

        // Reject an oversized body before reading it. ContentLength covers the
        // whole multipart body, hence the slack for boundaries and sibling fields.
        const long multipartSlack = 1024 * 1024;
        var fileSizeLimit = options.Value.MaximumFileSizeBytes;
        if (request.ContentLength is { } bodyLength && bodyLength > fileSizeLimit + multipartSlack)
        {
            throw new HttpException(
                StatusCodes.Status413PayloadTooLarge,
                "File too large",
                $"File size exceeds the limit of {fileSizeLimit} bytes.");
        }

        var boundary = HeaderUtilities.RemoveQuotes(
            MediaTypeHeaderValue.Parse(request.ContentType).Boundary).Value;

        if (string.IsNullOrEmpty(boundary))
        {
            return Results.BadRequest("Missing multipart boundary");
        }

        var reader = new MultipartReader(boundary, request.Body);
        MultipartSection? section;
        while ((section = await reader.ReadNextSectionAsync(cancellationToken)) != null)
        {
            var hasContentDisposition = ContentDispositionHeaderValue.TryParse(
                section.ContentDisposition, out var contentDisposition);

            if (hasContentDisposition &&
                contentDisposition != null &&
                contentDisposition.DispositionType.Equals("form-data") &&
                !string.IsNullOrEmpty(contentDisposition.FileName.Value))
            {
                var fileName = contentDisposition.FileName.Value!;
                var relativePath = string.IsNullOrEmpty(path) ? fileName : $"{path.TrimEnd('/')}/{fileName}";

                return await TranslateAsync(async () =>
                {
                    await service.UploadFileAsync(
                        serverId,
                        root,
                        relativePath,
                        section.Body,
                        cancellationToken);
                    return Results.Ok();
                });
            }
        }

        return Results.BadRequest("No file found in request.");
    }

    private static async Task<IResult> TranslateAsync(Func<Task<IResult>> operation)
    {
        try
        {
            return await operation();
        }
        catch (InstanceNotFoundException exception)
        {
            throw new HttpException(StatusCodes.Status404NotFound, "Instance not found", exception.Message);
        }
        catch (RootNotFoundException exception)
        {
            throw new HttpException(StatusCodes.Status404NotFound, "Root not found", exception.Message);
        }
        catch (ServerNotFoundException exception)
        {
            throw new HttpException(StatusCodes.Status404NotFound, "Server not found", exception.Message);
        }
        catch (FileNotFoundException exception)
        {
            throw new HttpException(StatusCodes.Status404NotFound, "File not found", exception.Message);
        }
        catch (InvalidRelativePathException exception)
        {
            throw new HttpException(StatusCodes.Status400BadRequest, "Invalid relative path", exception.Message);
        }
        catch (PermissionDeniedException exception)
        {
            throw new HttpException(StatusCodes.Status403Forbidden, "Permission denied", exception.Message);
        }
        catch (ServerOperationConflictException exception)
        {
            throw new HttpException(StatusCodes.Status409Conflict, "Lifecycle operation conflict", exception.Message);
        }
        catch (FileTooLargeException exception)
        {
            throw new HttpException(StatusCodes.Status413PayloadTooLarge, "File too large", exception.Message);
        }
        catch (UnsupportedFileException exception)
        {
            throw new HttpException(StatusCodes.Status415UnsupportedMediaType, "Unsupported file format", exception.Message);
        }
        catch (ServerOperationFailedException exception)
        {
            throw new HttpException(StatusCodes.Status502BadGateway, "Remote operation failed", exception.Message);
        }
        catch (RemoteOperationFailedException exception)
        {
            throw new HttpException(StatusCodes.Status502BadGateway, "Remote operation failed", exception.Message);
        }
        catch (ServerUnavailableException exception)
        {
            throw new HttpException(StatusCodes.Status503ServiceUnavailable, "Server unavailable", exception.Message);
        }
    }
}
