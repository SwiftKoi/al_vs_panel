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

public sealed record SaveFileContentRequest(string Path, string Content, DateTimeOffset? ExpectedModified = null);
public sealed record CreateDirectoryRequest(string Path);
public sealed record RenameRequest(string Path, string NewName);
public sealed record MoveRequest(string SourcePath, string DestinationPath);
public sealed record CompressRequest(IReadOnlyList<string> SourcePaths, string DestinationZipPath);
public sealed record ExtractRequest(string ZipPath, string DestinationDirectoryPath);
public sealed record DownloadArchiveRequest(IReadOnlyList<string> Paths);

public static class FileManagerEndpoints
{
    public static async Task<IResult> RootsAsync(
        string serverId,
        IFileManagerService service)
    {
        return await TranslateAsync(() => Task.FromResult(service.GetRoots(serverId)));
    }

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

    public static async Task<IResult> SearchAsync(
        string serverId,
        string root,
        IFileManagerService service,
        CancellationToken cancellationToken,
        string? q = null,
        string? path = null,
        bool recursive = false)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return Results.BadRequest("Search text (q) is required.");
        }

        return await TranslateAsync(() => service.SearchAsync(
            serverId, root, path ?? string.Empty, q, recursive, cancellationToken));
    }

    public static async Task<IResult> MeasureAsync(
        string serverId,
        string root,
        IFileManagerService service,
        CancellationToken cancellationToken,
        string? path = null)
    {
        return await TranslateAsync(() => service.MeasureAsync(serverId, root, path ?? string.Empty, cancellationToken));
    }

    // Raster formats only: they cannot run script. SVG is deliberately excluded because
    // opened directly it would execute in the panel's origin.
    private static readonly Dictionary<string, string> PreviewImageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".bmp"] = "image/bmp",
        [".ico"] = "image/x-icon"
    };

    public static async Task<IResult> DownloadAsync(
        string serverId,
        string root,
        HttpResponse response,
        IFileManagerService service,
        CancellationToken cancellationToken,
        string path,
        bool inline = false)
    {
        if (string.IsNullOrEmpty(path))
        {
            return Results.BadRequest("Path is required.");
        }

        string? previewType = null;
        if (inline && !PreviewImageTypes.TryGetValue(Path.GetExtension(path), out previewType))
        {
            return Results.Problem(
                title: "Preview not supported",
                detail: "Only PNG, JPEG, GIF, WebP, BMP and ICO images can be previewed.",
                statusCode: StatusCodes.Status415UnsupportedMediaType);
        }

        return await TranslateAsync(async () =>
        {
            // Validate before streaming: once Results.Stream starts, the 200 status and
            // headers are sent and a missing file or bad path can no longer become an
            // error response — the browser would save an empty or truncated file.
            var file = await service.GetFileInfoAsync(serverId, root, path, cancellationToken);
            var fileName = file.Name;

            if (previewType is not null)
            {
                // Inline image for the preview: correct type (the gateway sends nosniff),
                // no attachment, and a CSP that blocks everything if opened on its own.
                response.Headers.ContentDisposition = "inline";
                response.Headers.ContentSecurityPolicy = "default-src 'none'; img-src 'self'; sandbox";
                response.Headers.CacheControl = "private, max-age=60";
                return Results.Stream(async stream =>
                {
                    await service.DownloadFileAsync(serverId, root, path, stream, cancellationToken);
                }, previewType);
            }

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
                request.ExpectedModified,
                cancellationToken);

            // Return the new modification time so the editor can detect later changes.
            var saved = await service.GetFileInfoAsync(serverId, root, request.Path, cancellationToken);
            return Results.Ok(new { saved.Modified });
        });
    }

    public static async Task<IResult> UploadAsync(
        string serverId,
        string root,
        HttpRequest request,
        IFileManagerService service,
        IOptions<FileManagerOptions> options,
        CancellationToken cancellationToken,
        string? path = null,
        bool createFolders = false)
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
        var uploaded = 0;
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
                var body = section.Body;

                // Every file part is written; each one is size-limited on its own.
                await TranslateAsync(async () =>
                {
                    await service.UploadFileAsync(serverId, root, relativePath, body, cancellationToken, createFolders);
                    return Results.Ok();
                });
                uploaded++;
            }
        }

        return uploaded > 0 ? Results.Ok() : Results.BadRequest("No file found in request.");
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
        string path,
        bool permanent = false)
    {
        if (string.IsNullOrEmpty(path))
        {
            return Results.BadRequest("Path is required.");
        }

        return await TranslateAsync(async () =>
        {
            var trashed = await service.DeleteAsync(serverId, root, path, permanent, cancellationToken);
            return trashed is null ? Results.Ok() : Results.Ok(trashed);
        });
    }

    public static async Task<IResult> PrepareDownloadArchiveAsync(
        string serverId,
        string root,
        DownloadArchiveRequest request,
        IFileManagerService service,
        CancellationToken cancellationToken)
    {
        if (request?.Paths is not { Count: > 0 })
        {
            return Results.BadRequest("Paths are required.");
        }

        return await TranslateAsync(() => service.PrepareDownloadArchiveAsync(serverId, root, request.Paths, cancellationToken));
    }

    public static async Task<IResult> DownloadArchiveAsync(
        string serverId,
        string root,
        string archiveId,
        IFileManagerService service,
        CancellationToken cancellationToken,
        string? name = null)
    {
        var fileName = string.IsNullOrWhiteSpace(name) ? "download.zip" : Path.GetFileName(name);
        return await TranslateAsync(async () =>
        {
            // Checked before streaming so an expired archive is a 404, not an empty file.
            await service.EnsureDownloadArchiveAsync(serverId, root, archiveId, cancellationToken);
            return Results.Stream(
                async stream => await service.DownloadArchiveAsync(serverId, root, archiveId, stream, cancellationToken),
                "application/zip",
                fileDownloadName: fileName);
        });
    }

    public static async Task<IResult> ListTrashAsync(
        string serverId,
        string root,
        IFileManagerService service,
        CancellationToken cancellationToken)
    {
        return await TranslateAsync(() => service.ListTrashAsync(serverId, root, cancellationToken));
    }

    public static async Task<IResult> RestoreTrashAsync(
        string serverId,
        string root,
        string trashId,
        IFileManagerService service,
        CancellationToken cancellationToken)
    {
        return await TranslateAsync(() => service.RestoreTrashAsync(serverId, root, trashId, cancellationToken));
    }

    public static async Task<IResult> PurgeTrashEntryAsync(
        string serverId,
        string root,
        string trashId,
        IFileManagerService service,
        CancellationToken cancellationToken)
    {
        return await TranslateAsync(async () =>
        {
            await service.PurgeTrashAsync(serverId, root, trashId, cancellationToken);
            return Results.Ok();
        });
    }

    public static async Task<IResult> EmptyTrashAsync(
        string serverId,
        string root,
        IFileManagerService service,
        CancellationToken cancellationToken)
    {
        return await TranslateAsync(async () =>
        {
            await service.PurgeTrashAsync(serverId, root, null, cancellationToken);
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
        if (request == null || request.SourcePaths is not { Count: > 0 } || string.IsNullOrEmpty(request.DestinationZipPath))
        {
            return Results.BadRequest("SourcePaths and DestinationZipPath are required.");
        }

        return await TranslateAsync(async () =>
        {
            var taskId = await service.ArchiveAsync(serverId, root, request.SourcePaths, request.DestinationZipPath, cancellationToken);
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
        // An empty destination means the root folder itself.
        if (request == null || string.IsNullOrEmpty(request.ZipPath) || request.DestinationDirectoryPath is null)
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

    public static IResult GetRecentOperations(IFileManagerService service)
    {
        return Results.Ok(service.GetRecentOperations());
    }

    public static IResult CancelOperation(string taskId, IFileManagerService service)
    {
        return service.CancelOperation(taskId)
            ? Results.Accepted()
            : Results.NotFound($"Operation task '{taskId}' is not running.");
    }

    private static async Task<IResult> TranslateAsync(Func<Task<IResult>> operation)
    {
        try
        {
            return await operation();
        }
        catch (Exception exception) when (Translate(exception) is { } httpException)
        {
            throw httpException;
        }
    }

    private static async Task<IResult> TranslateAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return Results.Ok(await operation());
        }
        catch (Exception exception) when (Translate(exception) is { } httpException)
        {
            throw httpException;
        }
    }

    private static HttpException? Translate(Exception exception) => exception switch
    {
        InstanceNotFoundException => new HttpException(StatusCodes.Status404NotFound, "Instance not found", exception.Message),
        RootNotFoundException => new HttpException(StatusCodes.Status404NotFound, "Root not found", exception.Message),
        InvalidRelativePathException => new HttpException(StatusCodes.Status400BadRequest, "Invalid relative path", exception.Message),
        InvalidFileOperationException => new HttpException(StatusCodes.Status400BadRequest, "Invalid file operation", exception.Message),
        PermissionDeniedException => new HttpException(StatusCodes.Status403Forbidden, "Permission denied", exception.Message),
        FileTooLargeException => new HttpException(StatusCodes.Status413PayloadTooLarge, "File too large", exception.Message),
        UnsupportedFileException => new HttpException(StatusCodes.Status415UnsupportedMediaType, "Unsupported file format", exception.Message),
        FileNotFoundException => new HttpException(StatusCodes.Status404NotFound, "File not found", exception.Message),
        ItemAlreadyExistsException => new HttpException(StatusCodes.Status409Conflict, "Already exists", exception.Message),
        FileChangedException => new HttpException(StatusCodes.Status409Conflict, "File changed", exception.Message),
        TooManyOperationsException => new HttpException(StatusCodes.Status429TooManyRequests, "Too many operations", exception.Message),
        RemoteOperationFailedException => new HttpException(StatusCodes.Status502BadGateway, "Remote operation failed", exception.Message),
        _ => null
    };
}
