using System.IO;
using AlegacyWebPanel.Core.Errors;
using AlegacyWebPanel.Modules.FileManager.Configuration;
using AlegacyWebPanel.Modules.FileManager.Exceptions;
using AlegacyWebPanel.Modules.FileManager.Services;
using AlegacyWebPanel.Modules.RemoteOperations.Exceptions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace AlegacyWebPanel.Modules.FileManager.Endpoints;

public sealed record SaveFileContentRequest(string Path, string Content);
public sealed record CreateDirectoryRequest(string Path);
public sealed record RenameRequest(string Path, string NewName);
public sealed record MoveRequest(string SourcePath, string DestinationPath);
public sealed record CompressRequest(string SourcePath, string DestinationZipPath);
public sealed record ExtractRequest(string ZipPath, string DestinationDirectoryPath);

public static class FileManagerEndpoints
{
    public static async Task<IResult> ListAsync(
        string serverId,
        string root,
        IFileManagerService service,
        CancellationToken cancellationToken,
        string? path = null)
    {
        return await TranslateAsync(() => service.GetDirectoryListingAsync(
            serverId,
            root,
            path ?? string.Empty,
            cancellationToken));
    }

    public static async Task<IResult> DownloadAsync(
        string serverId,
        string root,
        IFileManagerService service,
        CancellationToken cancellationToken,
        string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return Results.BadRequest("Path is required.");
        }

        var fileName = Path.GetFileName(path);

        return await TranslateAsync(async () =>
        {
            // Verify access synchronously first to capture exceptions before stream execution
            return Results.Stream(async stream =>
            {
                await service.DownloadFileAsync(serverId, root, path, stream, cancellationToken);
            }, "application/octet-stream", fileDownloadName: fileName);
        });
    }

    public static async Task<IResult> GetContentAsync(
        string serverId,
        string root,
        IFileManagerService service,
        CancellationToken cancellationToken,
        string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return Results.BadRequest("Path is required.");
        }

        return await TranslateAsync(() => service.GetTextContentAsync(
            serverId,
            root,
            path,
            cancellationToken));
    }

    public static async Task<IResult> SaveContentAsync(
        string serverId,
        string root,
        SaveFileContentRequest request,
        IFileManagerService service,
        CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrEmpty(request.Path))
        {
            return Results.BadRequest("Path is required.");
        }

        return await TranslateAsync(async () =>
        {
            await service.SaveTextContentAsync(
                serverId,
                root,
                request.Path,
                request.Content ?? string.Empty,
                cancellationToken);
            return Results.Ok();
        });
    }

    public static async Task<IResult> UploadAsync(
        string serverId,
        string root,
        HttpRequest request,
        IFileManagerService service,
        IOptions<FileManagerOptions> options,
        CancellationToken cancellationToken,
        string? path = null)
    {
        // The antiforgery middleware records the validation outcome on the request.
        // Touching the form without consulting it throws InvalidOperationException
        // ("invalid anti-forgery token") and surfaces as HTTP 500 — so check it
        // first and reject a stale token with a clean, retryable 400.
        var antiforgery = request.HttpContext.Features.Get<IAntiforgeryValidationFeature>();
        if (antiforgery is { IsValid: false })
        {
            return Results.Problem(
                title: "Invalid anti-forgery token",
                detail: "The anti-forgery token is missing, expired, or was issued for a previous session. Fetch a new token from /auth/csrf and retry.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // Deliberately not request.HasFormContentType: that property touches the
        // form feature itself, which is exactly what throws on an unvalidated form.
        if (request.ContentType is null ||
            !request.ContentType.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest("Request must be multipart/form-data");
        }

        // Reject an oversized body before reading it. Streaming it would abort halfway
        // (the helper process dies with a broken pipe) and could leave a truncated file
        // behind. ContentLength covers the whole multipart body, hence the slack for
        // boundaries and sibling form fields.
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

    public static async Task<IResult> CreateDirectoryAsync(
        string serverId,
        string root,
        CreateDirectoryRequest request,
        IFileManagerService service,
        CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrEmpty(request.Path))
        {
            return Results.BadRequest("Path is required.");
        }

        return await TranslateAsync(async () =>
        {
            await service.CreateDirectoryAsync(serverId, root, request.Path, cancellationToken);
            return Results.Ok();
        });
    }

    public static async Task<IResult> RenameAsync(
        string serverId,
        string root,
        RenameRequest request,
        IFileManagerService service,
        CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrEmpty(request.Path) || string.IsNullOrEmpty(request.NewName))
        {
            return Results.BadRequest("Path and NewName are required.");
        }

        return await TranslateAsync(async () =>
        {
            await service.RenameAsync(serverId, root, request.Path, request.NewName, cancellationToken);
            return Results.Ok();
        });
    }

    public static async Task<IResult> MoveAsync(
        string serverId,
        string root,
        MoveRequest request,
        IFileManagerService service,
        CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrEmpty(request.SourcePath) || string.IsNullOrEmpty(request.DestinationPath))
        {
            return Results.BadRequest("SourcePath and DestinationPath are required.");
        }

        return await TranslateAsync(async () =>
        {
            await service.MoveAsync(serverId, root, request.SourcePath, request.DestinationPath, cancellationToken);
            return Results.Ok();
        });
    }

    public static async Task<IResult> DeleteAsync(
        string serverId,
        string root,
        IFileManagerService service,
        CancellationToken cancellationToken,
        string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return Results.BadRequest("Path is required.");
        }

        return await TranslateAsync(async () =>
        {
            await service.DeleteAsync(serverId, root, path, cancellationToken);
            return Results.Ok();
        });
    }

    public static async Task<IResult> CompressAsync(
        string serverId,
        string root,
        CompressRequest request,
        IFileManagerService service,
        CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrEmpty(request.SourcePath) || string.IsNullOrEmpty(request.DestinationZipPath))
        {
            return Results.BadRequest("SourcePath and DestinationZipPath are required.");
        }

        return await TranslateAsync(async () =>
        {
            var taskId = await service.ArchiveAsync(serverId, root, request.SourcePath, request.DestinationZipPath, cancellationToken);
            return Results.Accepted($"/api/servers/operations/{taskId}", new { TaskId = taskId });
        });
    }

    public static async Task<IResult> ExtractAsync(
        string serverId,
        string root,
        ExtractRequest request,
        IFileManagerService service,
        CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrEmpty(request.ZipPath) || string.IsNullOrEmpty(request.DestinationDirectoryPath))
        {
            return Results.BadRequest("ZipPath and DestinationDirectoryPath are required.");
        }

        return await TranslateAsync(async () =>
        {
            var taskId = await service.ExtractAsync(serverId, root, request.ZipPath, request.DestinationDirectoryPath, cancellationToken);
            return Results.Accepted($"/api/servers/operations/{taskId}", new { TaskId = taskId });
        });
    }

    public static IResult GetOperationStatusAsync(
        string taskId,
        IFileManagerService service)
    {
        if (string.IsNullOrEmpty(taskId))
        {
            return Results.BadRequest("TaskId is required.");
        }

        var operation = service.GetOperationStatus(taskId);
        if (operation == null)
        {
            return Results.NotFound($"Operation task '{taskId}' was not found.");
        }

        return Results.Ok(operation);
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
        catch (InvalidRelativePathException exception)
        {
            throw new HttpException(StatusCodes.Status400BadRequest, "Invalid relative path", exception.Message);
        }
        catch (PermissionDeniedException exception)
        {
            throw new HttpException(StatusCodes.Status403Forbidden, "Permission denied", exception.Message);
        }
        catch (FileTooLargeException exception)
        {
            throw new HttpException(StatusCodes.Status413PayloadTooLarge, "File too large", exception.Message);
        }
        catch (UnsupportedFileException exception)
        {
            throw new HttpException(StatusCodes.Status415UnsupportedMediaType, "Unsupported file format", exception.Message);
        }
        catch (FileNotFoundException exception)
        {
            throw new HttpException(StatusCodes.Status404NotFound, "File not found", exception.Message);
        }
        catch (RemoteOperationFailedException exception)
        {
            throw new HttpException(StatusCodes.Status502BadGateway, "Remote operation failed", exception.Message);
        }
    }

    private static async Task<IResult> TranslateAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return Results.Ok(await operation());
        }
        catch (InstanceNotFoundException exception)
        {
            throw new HttpException(StatusCodes.Status404NotFound, "Instance not found", exception.Message);
        }
        catch (RootNotFoundException exception)
        {
            throw new HttpException(StatusCodes.Status404NotFound, "Root not found", exception.Message);
        }
        catch (InvalidRelativePathException exception)
        {
            throw new HttpException(StatusCodes.Status400BadRequest, "Invalid relative path", exception.Message);
        }
        catch (PermissionDeniedException exception)
        {
            throw new HttpException(StatusCodes.Status403Forbidden, "Permission denied", exception.Message);
        }
        catch (FileTooLargeException exception)
        {
            throw new HttpException(StatusCodes.Status413PayloadTooLarge, "File too large", exception.Message);
        }
        catch (UnsupportedFileException exception)
        {
            throw new HttpException(StatusCodes.Status415UnsupportedMediaType, "Unsupported file format", exception.Message);
        }
        catch (FileNotFoundException exception)
        {
            throw new HttpException(StatusCodes.Status404NotFound, "File not found", exception.Message);
        }
        catch (RemoteOperationFailedException exception)
        {
            throw new HttpException(StatusCodes.Status502BadGateway, "Remote operation failed", exception.Message);
        }
    }
}
