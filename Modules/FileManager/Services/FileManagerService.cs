using System.IO;
using System.Text;
using AlegacyWebPanel.Modules.FileManager.Configuration;
using AlegacyWebPanel.Modules.FileManager.Contracts;
using AlegacyWebPanel.Modules.FileManager.Exceptions;
using AlegacyWebPanel.Modules.FileManager.Persistence;
using Microsoft.Extensions.Options;

namespace AlegacyWebPanel.Modules.FileManager.Services;

public sealed class FileManagerService(
    IFileRepository fileRepository,
    IBackgroundOperationTracker backgroundOperationTracker,
    IOptions<FileManagerOptions> options,
    ILogger<FileManagerService> logger)
    : IFileManagerService
{
    // Strict decoder: invalid bytes throw instead of silently becoming U+FFFD,
    // which would then be written back on save and corrupt the file.
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly FileManagerOptions _options = options.Value;

    public IReadOnlyList<FileRootDto> GetRoots(string serverId)
    {
        if (!_options.Instances.TryGetValue(serverId, out var instance))
        {
            throw new InstanceNotFoundException(serverId);
        }

        return RootsOf(instance);
    }

    public async Task<DirectoryListingDto> GetDirectoryListingAsync(
        string serverId,
        string rootId,
        string relativePath,
        CancellationToken cancellationToken)
    {
        var (instance, root) = ResolveRoot(serverId, rootId);
        ValidateRelativePath(relativePath);

        logger.LogDebug("Listing files for server {ServerId} root {RootId}", serverId, rootId);
        var listing = await fileRepository.ListAsync(
            root.Operation, relativePath, _options.MaximumListingEntries, cancellationToken);

        return new DirectoryListingDto(
            relativePath, RootsOf(instance), listing.Entries, listing.Truncated, listing.Skipped, _options.MaximumFileSizeBytes, _options.TrashRetentionDays);
    }

    public async Task<SearchResponseDto> SearchAsync(
        string serverId,
        string rootId,
        string relativePath,
        string query,
        bool recursive,
        CancellationToken cancellationToken)
    {
        var (_, root) = ResolveRoot(serverId, rootId);
        ValidateRelativePath(relativePath);

        query = query.Trim();
        if (query.Length == 0 || query.Length > 200 || query.Any(char.IsControl))
        {
            throw new InvalidFileOperationException("Search text must be 1–200 printable characters.");
        }

        logger.LogDebug("Searching files for server {ServerId} root {RootId}", serverId, rootId);
        return await fileRepository.SearchAsync(
            root.Operation, relativePath, query, recursive, _options.MaximumSearchResults, cancellationToken);
    }

    public async Task<FolderSizeDto> MeasureAsync(string serverId, string rootId, string relativePath, CancellationToken cancellationToken)
    {
        var (_, root) = ResolveRoot(serverId, rootId);
        ValidateRelativePath(relativePath);
        return await fileRepository.MeasureAsync(root.Operation, relativePath, cancellationToken);
    }

    public async Task<FileEntryDto> GetFileInfoAsync(
        string serverId,
        string rootId,
        string relativePath,
        CancellationToken cancellationToken)
    {
        var (_, root) = ResolveRoot(serverId, rootId);
        ValidateRelativePath(relativePath);

        var entry = await fileRepository.StatAsync(root.Operation, relativePath, cancellationToken)
            ?? throw new FileNotFoundException($"File '{relativePath}' was not found.");
        if (entry.IsFolder)
        {
            throw new InvalidFileOperationException($"'{relativePath}' is a folder, not a file.");
        }

        return entry;
    }

    public async Task DownloadFileAsync(
        string serverId,
        string rootId,
        string relativePath,
        Stream destination,
        CancellationToken cancellationToken)
    {
        var (_, root) = ResolveRoot(serverId, rootId);
        ValidateRelativePath(relativePath);

        logger.LogDebug("Downloading file for server {ServerId} root {RootId}", serverId, rootId);
        await fileRepository.ReadAsync(root.Operation, relativePath, destination, cancellationToken);
    }

    public async Task UploadFileAsync(
        string serverId,
        string rootId,
        string relativePath,
        Stream source,
        CancellationToken cancellationToken,
        bool createFolders = false)
    {
        var root = ResolveWritableRoot(serverId, rootId);
        ValidateRelativePath(relativePath);

        // Wrap stream to enforce limit
        using var bounded = new BoundedStream(source, _options.MaximumFileSizeBytes);

        logger.LogInformation("Uploading file to server {ServerId} root {RootId}", serverId, rootId);
        await fileRepository.WriteAsync(root.Operation, relativePath, bounded, cancellationToken, createFolders);
    }

    public Task<DownloadArchiveDto> PrepareDownloadArchiveAsync(
        string serverId,
        string rootId,
        IReadOnlyList<string> sourcePaths,
        CancellationToken cancellationToken)
    {
        // The archive is written inside the root, so it needs a writable root.
        var root = ResolveWritableRoot(serverId, rootId);
        var sources = sourcePaths.Where(path => !string.IsNullOrWhiteSpace(path)).ToList();
        if (sources.Count == 0)
        {
            throw new InvalidRelativePathException("At least one source path is required.");
        }

        foreach (var path in sources)
        {
            ValidateRelativePath(path);
        }

        var archiveId = Guid.NewGuid().ToString("N");
        var description = sources.Count == 1
            ? $"Preparing download of '{sources[0]}'"
            : $"Preparing download of {sources.Count} items";
        var taskId = backgroundOperationTracker.StartTracking(
            description,
            null,
            (ct, progress) => fileRepository.ArchiveForDownloadAsync(root.Operation, archiveId, sources, progress, ct));

        return Task.FromResult(new DownloadArchiveDto(taskId, archiveId));
    }

    public async Task EnsureDownloadArchiveAsync(string serverId, string rootId, string archiveId, CancellationToken cancellationToken)
    {
        var root = ResolveWritableRoot(serverId, rootId);
        ValidateArchiveId(archiveId);
        if (await fileRepository.StatAsync(root.Operation, $".panel-tmp/{archiveId}.zip", cancellationToken) is null)
        {
            throw new FileNotFoundException("The prepared download has expired or was already downloaded.");
        }
    }

    public async Task DownloadArchiveAsync(
        string serverId,
        string rootId,
        string archiveId,
        Stream destination,
        CancellationToken cancellationToken)
    {
        var root = ResolveWritableRoot(serverId, rootId);
        ValidateArchiveId(archiveId);
        try
        {
            await fileRepository.ReadAsync(root.Operation, $".panel-tmp/{archiveId}.zip", destination, cancellationToken);
        }
        finally
        {
            // One-off: remove it even if the client aborted. Stale leftovers are purged by the helper.
            try
            {
                await fileRepository.DeleteDownloadArchiveAsync(root.Operation, archiveId, CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Could not remove download archive {ArchiveId}", archiveId);
            }
        }
    }

    private static void ValidateArchiveId(string archiveId)
    {
        if (archiveId.Length != 32 || !archiveId.All(char.IsAsciiHexDigitLower))
        {
            throw new InvalidRelativePathException("Invalid archive id.");
        }
    }

    public async Task<FileContentDto> GetTextContentAsync(
        string serverId,
        string rootId,
        string relativePath,
        CancellationToken cancellationToken)
    {
        var (_, root) = ResolveRoot(serverId, rootId);
        ValidateRelativePath(relativePath);

        var fileEntry = await fileRepository.StatAsync(root.Operation, relativePath, cancellationToken);
        if (fileEntry is null || fileEntry.IsFolder)
        {
            throw new FileNotFoundException($"File '{relativePath}' was not found.");
        }

        if (fileEntry.Size > _options.MaximumTextFileSizeBytes)
        {
            throw new FileTooLargeException($"File size exceeds the limit of {_options.MaximumTextFileSizeBytes} bytes for text editing.");
        }

        using var ms = new MemoryStream();
        await fileRepository.ReadAsync(root.Operation, relativePath, ms, cancellationToken);
        if (ms.Length > _options.MaximumTextFileSizeBytes)
        {
            // The file grew between the size check and the read.
            throw new FileTooLargeException($"File size exceeds the limit of {_options.MaximumTextFileSizeBytes} bytes for text editing.");
        }

        var bytes = ms.GetBuffer().AsSpan(0, (int)ms.Length);
        if (bytes.Contains((byte)0))
        {
            throw new UnsupportedFileException("The file appears to be a binary file and cannot be opened in the text editor.");
        }

        string content;
        try
        {
            // GetString keeps a leading BOM as U+FEFF, and saving encodes it back to
            // the same three bytes, so files with and without a BOM round-trip unchanged.
            content = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            throw new UnsupportedFileException("The file is not valid UTF-8 text and cannot be edited without corrupting it.");
        }

        return new FileContentDto(content, fileEntry.Modified);
    }

    public async Task SaveTextContentAsync(
        string serverId,
        string rootId,
        string relativePath,
        string content,
        DateTimeOffset? expectedModified,
        CancellationToken cancellationToken)
    {
        var root = ResolveWritableRoot(serverId, rootId);
        ValidateRelativePath(relativePath);

        var bytes = Encoding.UTF8.GetBytes(content);
        if (bytes.Length > _options.MaximumTextFileSizeBytes)
        {
            throw new FileTooLargeException($"Content size exceeds the limit of {_options.MaximumTextFileSizeBytes} bytes.");
        }

        if (expectedModified is { } expected)
        {
            // Refuse to overwrite a file someone (often the game server itself) changed
            // after it was opened in the editor.
            var current = await fileRepository.StatAsync(root.Operation, relativePath, cancellationToken);
            if (current is not null && current.Modified != expected)
            {
                throw new FileChangedException(
                    $"'{relativePath}' was changed on the server after it was opened (at {current.Modified:u}). Reload it before saving.");
            }
        }

        using var ms = new MemoryStream(bytes);
        logger.LogInformation("Saving text content for server {ServerId} root {RootId}", serverId, rootId);
        await fileRepository.WriteAsync(root.Operation, relativePath, ms, cancellationToken);
    }

    public async Task CreateDirectoryAsync(
        string serverId,
        string rootId,
        string relativePath,
        CancellationToken cancellationToken)
    {
        var root = ResolveWritableRoot(serverId, rootId);
        ValidateRelativePath(relativePath);

        logger.LogInformation("Creating directory for server {ServerId} root {RootId}", serverId, rootId);
        await fileRepository.CreateDirectoryAsync(root.Operation, relativePath, cancellationToken);
    }

    public async Task RenameAsync(
        string serverId,
        string rootId,
        string relativePath,
        string newName,
        CancellationToken cancellationToken)
    {
        var root = ResolveWritableRoot(serverId, rootId);
        ValidateRelativePath(relativePath);

        if (string.IsNullOrEmpty(newName) || newName.Contains('/') || newName.Contains('\\') || newName.Contains('\0') || newName.Any(char.IsControl) || newName == ".." || newName == ".")
        {
            throw new InvalidRelativePathException("Invalid name for rename operation.");
        }

        logger.LogInformation("Renaming item for server {ServerId} root {RootId}", serverId, rootId);
        await fileRepository.RenameAsync(root.Operation, relativePath, newName, cancellationToken);
    }

    public async Task MoveAsync(
        string serverId,
        string rootId,
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        var root = ResolveWritableRoot(serverId, rootId);
        ValidateRelativePath(sourcePath);
        ValidateRelativePath(destinationPath);

        logger.LogInformation("Moving item for server {ServerId} root {RootId}", serverId, rootId);
        await fileRepository.MoveAsync(root.Operation, sourcePath, destinationPath, cancellationToken);
    }

    public async Task<TrashEntryDto?> DeleteAsync(
        string serverId,
        string rootId,
        string relativePath,
        bool permanent,
        CancellationToken cancellationToken)
    {
        var root = ResolveWritableRoot(serverId, rootId);
        ValidateRelativePath(relativePath);
        if (string.IsNullOrWhiteSpace(relativePath.Trim('/')))
        {
            throw new InvalidRelativePathException("The root folder itself cannot be deleted.");
        }

        if (permanent)
        {
            logger.LogInformation("Permanently deleting item for server {ServerId} root {RootId}", serverId, rootId);
            await fileRepository.DeleteAsync(root.Operation, relativePath, cancellationToken);
            return null;
        }

        logger.LogInformation("Moving item to trash for server {ServerId} root {RootId}", serverId, rootId);
        return await fileRepository.TrashAsync(root.Operation, relativePath, _options.TrashRetentionDays, cancellationToken);
    }

    public async Task<IReadOnlyList<TrashEntryDto>> ListTrashAsync(string serverId, string rootId, CancellationToken cancellationToken)
    {
        var root = ResolveWritableRoot(serverId, rootId);
        return await fileRepository.ListTrashAsync(root.Operation, _options.TrashRetentionDays, cancellationToken);
    }

    public async Task<TrashEntryDto> RestoreTrashAsync(string serverId, string rootId, string trashId, CancellationToken cancellationToken)
    {
        var root = ResolveWritableRoot(serverId, rootId);
        ValidateTrashId(trashId);
        logger.LogInformation("Restoring trash entry for server {ServerId} root {RootId}", serverId, rootId);
        return await fileRepository.RestoreTrashAsync(root.Operation, trashId, cancellationToken);
    }

    public async Task PurgeTrashAsync(string serverId, string rootId, string? trashId, CancellationToken cancellationToken)
    {
        var root = ResolveWritableRoot(serverId, rootId);
        if (trashId is not null)
        {
            ValidateTrashId(trashId);
        }

        logger.LogInformation("Purging trash for server {ServerId} root {RootId}", serverId, rootId);
        await fileRepository.PurgeTrashAsync(root.Operation, trashId, cancellationToken);
    }

    private static void ValidateTrashId(string trashId)
    {
        if (string.IsNullOrEmpty(trashId) || !trashId.All(c => char.IsAsciiDigit(c) || c == '-'))
        {
            throw new InvalidRelativePathException("Invalid trash id.");
        }
    }

    public Task<string> ArchiveAsync(
        string serverId,
        string rootId,
        IReadOnlyList<string> sourcePaths,
        string zipPath,
        CancellationToken cancellationToken)
    {
        var root = ResolveWritableRoot(serverId, rootId);

        var sources = sourcePaths.Where(path => !string.IsNullOrWhiteSpace(path)).ToList();
        if (sources.Count == 0)
        {
            throw new InvalidRelativePathException("At least one source path is required.");
        }

        foreach (var path in sources)
        {
            ValidateRelativePath(path);
        }
        ValidateRelativePath(zipPath);

        var description = sources.Count == 1
            ? $"Compressing '{sources[0]}' to '{zipPath}' in root '{rootId}'"
            : $"Compressing {sources.Count} items to '{zipPath}' in root '{rootId}'";

        logger.LogInformation("Starting compression for server {ServerId} root {RootId}", serverId, rootId);
        var taskId = backgroundOperationTracker.StartTracking(
            description,
            new OperationTarget(serverId, rootId, ParentFolder(zipPath)),
            (ct, progress) => fileRepository.ArchiveAsync(root.Operation, sources, zipPath, progress, ct));

        return Task.FromResult(taskId);
    }

    public Task<string> ExtractAsync(
        string serverId,
        string rootId,
        string zipPath,
        string destPath,
        CancellationToken cancellationToken)
    {
        var root = ResolveWritableRoot(serverId, rootId);
        ValidateRelativePath(zipPath);
        ValidateRelativePath(destPath);

        logger.LogInformation("Starting extraction for server {ServerId} root {RootId}", serverId, rootId);
        var taskId = backgroundOperationTracker.StartTracking(
            $"Extracting '{zipPath}' to '{(destPath.Length == 0 ? "/" : destPath)}' in root '{rootId}'",
            new OperationTarget(serverId, rootId, destPath.Trim('/')),
            (ct, progress) => fileRepository.ExtractAsync(
                root.Operation,
                zipPath,
                destPath,
                _options.MaximumArchiveSizeBytes,
                _options.MaximumArchiveEntries,
                progress,
                ct));

        return Task.FromResult(taskId);
    }

    public TrackedOperationDto? GetOperationStatus(string taskId)
    {
        return backgroundOperationTracker.GetStatus(taskId);
    }

    public IReadOnlyList<TrackedOperationDto> GetRecentOperations()
    {
        return backgroundOperationTracker.GetRecent();
    }

    public bool CancelOperation(string taskId)
    {
        logger.LogInformation("Cancelling background operation {TaskId}", taskId);
        return backgroundOperationTracker.Cancel(taskId);
    }

    private static string ParentFolder(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/').Trim('/');
        var lastSlash = normalized.LastIndexOf('/');
        return lastSlash == -1 ? string.Empty : normalized[..lastSlash];
    }

    private (ServerInstanceRoots Instance, FileRootConfig Root) ResolveRoot(string serverId, string rootId)
    {
        if (!_options.Instances.TryGetValue(serverId, out var instance))
        {
            throw new InstanceNotFoundException(serverId);
        }

        if (!instance.Roots.TryGetValue(rootId, out var root))
        {
            throw new RootNotFoundException(rootId);
        }

        return (instance, root);
    }

    private FileRootConfig ResolveWritableRoot(string serverId, string rootId)
    {
        var (_, root) = ResolveRoot(serverId, rootId);
        if (!root.IsWritable)
        {
            throw new PermissionDeniedException($"The root '{rootId}' is read-only.");
        }

        return root;
    }

    private static IReadOnlyList<FileRootDto> RootsOf(ServerInstanceRoots instance) =>
        instance.Roots.Select(r => new FileRootDto(r.Key, r.Value.DisplayName, r.Value.IsWritable, r.Value.ProtectedPaths)).ToList();

    private static void ValidateRelativePath(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath)) return;

        if (relativePath.Contains('\0') || relativePath.Any(char.IsControl))
        {
            throw new InvalidRelativePathException("Relative path contains invalid characters.");
        }

        var normalized = relativePath.Replace('\\', '/');
        var segments = normalized.Split('/');

        // The trash is only reachable through the trash endpoints.
        if (segments.FirstOrDefault(s => s.Length > 0) is ".trash" or ".panel-tmp")
        {
            throw new InvalidRelativePathException("The trash folder can only be managed from the trash view.");
        }

        if (segments.Any(s => s == ".." || s == "."))
        {
            throw new InvalidRelativePathException("Path traversal attempts are not permitted.");
        }

        // Colons are legal in Linux file names (timestamped logs, crash dumps); the
        // helper confines every path to the root regardless.
        if (normalized.StartsWith('/') || normalized.StartsWith('~'))
        {
            throw new InvalidRelativePathException("Absolute path references are not permitted.");
        }
    }
}
