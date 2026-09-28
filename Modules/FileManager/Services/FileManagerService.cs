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

        return new DirectoryListingDto(relativePath, RootsOf(instance), listing.Entries, listing.Truncated, listing.Skipped);
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
        CancellationToken cancellationToken)
    {
        var root = ResolveWritableRoot(serverId, rootId);
        ValidateRelativePath(relativePath);

        // Wrap stream to enforce limit
        using var bounded = new BoundedStream(source, _options.MaximumFileSizeBytes);

        logger.LogInformation("Uploading file to server {ServerId} root {RootId}", serverId, rootId);
        await fileRepository.WriteAsync(root.Operation, relativePath, bounded, cancellationToken);
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

    public async Task DeleteAsync(
        string serverId,
        string rootId,
        string relativePath,
        CancellationToken cancellationToken)
    {
        var root = ResolveWritableRoot(serverId, rootId);
        ValidateRelativePath(relativePath);

        logger.LogInformation("Deleting item for server {ServerId} root {RootId}", serverId, rootId);
        await fileRepository.DeleteAsync(root.Operation, relativePath, cancellationToken);
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
            ct => fileRepository.ArchiveAsync(root.Operation, sources, zipPath, ct));

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
            ct => fileRepository.ExtractAsync(
                root.Operation,
                zipPath,
                destPath,
                _options.MaximumArchiveSizeBytes,
                _options.MaximumArchiveEntries,
                ct));

        return Task.FromResult(taskId);
    }

    public TrackedOperationDto? GetOperationStatus(string taskId)
    {
        return backgroundOperationTracker.GetStatus(taskId);
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
        instance.Roots.Select(r => new FileRootDto(r.Key, r.Value.DisplayName, r.Value.IsWritable)).ToList();

    private static void ValidateRelativePath(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath)) return;

        if (relativePath.Contains('\0') || relativePath.Any(char.IsControl))
        {
            throw new InvalidRelativePathException("Relative path contains invalid characters.");
        }

        var normalized = relativePath.Replace('\\', '/');
        var segments = normalized.Split('/');

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
