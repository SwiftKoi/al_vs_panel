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
    private readonly FileManagerOptions _options = options.Value;

    public async Task<DirectoryListingDto> GetDirectoryListingAsync(
        string serverId,
        string rootId,
        string relativePath,
        CancellationToken cancellationToken)
    {
        if (!_options.Instances.TryGetValue(serverId, out var instance))
        {
            throw new InstanceNotFoundException(serverId);
        }

        if (!instance.Roots.TryGetValue(rootId, out var root))
        {
            throw new RootNotFoundException(rootId);
        }

        ValidateRelativePath(relativePath);

        // Fetch directory entries from target
        logger.LogDebug("Listing files for server {ServerId} root {RootId}", serverId, rootId);
        var entries = await fileRepository.ListAsync(root.Operation, relativePath, cancellationToken);

        // Build list of all roots configured for this server instance
        var rootsList = instance.Roots.Select(r => new FileRootDto(
            r.Key,
            r.Value.DisplayName,
            r.Value.IsWritable)).ToList();

        return new DirectoryListingDto(relativePath, rootsList, entries);
    }

    public async Task DownloadFileAsync(
        string serverId,
        string rootId,
        string relativePath,
        Stream destination,
        CancellationToken cancellationToken)
    {
        if (!_options.Instances.TryGetValue(serverId, out var instance))
        {
            throw new InstanceNotFoundException(serverId);
        }

        if (!instance.Roots.TryGetValue(rootId, out var root))
        {
            throw new RootNotFoundException(rootId);
        }

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
        if (!_options.Instances.TryGetValue(serverId, out var instance))
        {
            throw new InstanceNotFoundException(serverId);
        }

        if (!instance.Roots.TryGetValue(rootId, out var root))
        {
            throw new RootNotFoundException(rootId);
        }

        if (!root.IsWritable)
        {
            throw new PermissionDeniedException($"The root '{rootId}' is read-only.");
        }

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
        if (!_options.Instances.TryGetValue(serverId, out var instance))
        {
            throw new InstanceNotFoundException(serverId);
        }

        if (!instance.Roots.TryGetValue(rootId, out var root))
        {
            throw new RootNotFoundException(rootId);
        }

        ValidateRelativePath(relativePath);

        // Resolve parent directory and file name to check file metadata
        var parentPath = string.Empty;
        var fileName = relativePath;
        var lastSlash = relativePath.Replace('\\', '/').LastIndexOf('/');
        if (lastSlash != -1)
        {
            parentPath = relativePath[..lastSlash];
            fileName = relativePath[(lastSlash + 1)..];
        }

        var listing = await fileRepository.ListAsync(root.Operation, parentPath, cancellationToken);
        var fileEntry = listing.FirstOrDefault(e => !e.IsFolder && e.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase));
        if (fileEntry == null)
        {
            throw new FileNotFoundException($"File '{relativePath}' was not found.");
        }

        if (fileEntry.Size > _options.MaximumTextFileSizeBytes)
        {
            throw new FileTooLargeException($"File size exceeds the limit of {_options.MaximumTextFileSizeBytes} bytes for text editing.");
        }

        using var ms = new MemoryStream();
        await fileRepository.ReadAsync(root.Operation, relativePath, ms, cancellationToken);
        
        ms.Position = 0;
        var buffer = new byte[Math.Min(8192, ms.Length)];
        var read = await ms.ReadAsync(buffer, cancellationToken);
        for (int i = 0; i < read; i++)
        {
            if (buffer[i] == 0)
            {
                throw new UnsupportedFileException("The file appears to be a binary file and cannot be opened in the text editor.");
            }
        }

        ms.Position = 0;
        using var reader = new StreamReader(ms, Encoding.UTF8);
        var content = await reader.ReadToEndAsync(cancellationToken);

        return new FileContentDto(content);
    }

    public async Task SaveTextContentAsync(
        string serverId,
        string rootId,
        string relativePath,
        string content,
        CancellationToken cancellationToken)
    {
        if (!_options.Instances.TryGetValue(serverId, out var instance))
        {
            throw new InstanceNotFoundException(serverId);
        }

        if (!instance.Roots.TryGetValue(rootId, out var root))
        {
            throw new RootNotFoundException(rootId);
        }

        if (!root.IsWritable)
        {
            throw new PermissionDeniedException($"The root '{rootId}' is read-only.");
        }

        ValidateRelativePath(relativePath);

        var bytes = Encoding.UTF8.GetBytes(content);
        if (bytes.Length > _options.MaximumTextFileSizeBytes)
        {
            throw new FileTooLargeException($"Content size exceeds the limit of {_options.MaximumTextFileSizeBytes} bytes.");
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
        if (!_options.Instances.TryGetValue(serverId, out var instance))
        {
            throw new InstanceNotFoundException(serverId);
        }

        if (!instance.Roots.TryGetValue(rootId, out var root))
        {
            throw new RootNotFoundException(rootId);
        }

        if (!root.IsWritable)
        {
            throw new PermissionDeniedException($"The root '{rootId}' is read-only.");
        }

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
        if (!_options.Instances.TryGetValue(serverId, out var instance))
        {
            throw new InstanceNotFoundException(serverId);
        }

        if (!instance.Roots.TryGetValue(rootId, out var root))
        {
            throw new RootNotFoundException(rootId);
        }

        if (!root.IsWritable)
        {
            throw new PermissionDeniedException($"The root '{rootId}' is read-only.");
        }

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
        if (!_options.Instances.TryGetValue(serverId, out var instance))
        {
            throw new InstanceNotFoundException(serverId);
        }

        if (!instance.Roots.TryGetValue(rootId, out var root))
        {
            throw new RootNotFoundException(rootId);
        }

        if (!root.IsWritable)
        {
            throw new PermissionDeniedException($"The root '{rootId}' is read-only.");
        }

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
        if (!_options.Instances.TryGetValue(serverId, out var instance))
        {
            throw new InstanceNotFoundException(serverId);
        }

        if (!instance.Roots.TryGetValue(rootId, out var root))
        {
            throw new RootNotFoundException(rootId);
        }

        if (!root.IsWritable)
        {
            throw new PermissionDeniedException($"The root '{rootId}' is read-only.");
        }

        ValidateRelativePath(relativePath);

        logger.LogInformation("Deleting item for server {ServerId} root {RootId}", serverId, rootId);
        await fileRepository.DeleteAsync(root.Operation, relativePath, cancellationToken);
    }

    public Task<string> ArchiveAsync(
        string serverId,
        string rootId,
        string relativePath,
        string zipPath,
        CancellationToken cancellationToken)
    {
        if (!_options.Instances.TryGetValue(serverId, out var instance))
        {
            throw new InstanceNotFoundException(serverId);
        }

        if (!instance.Roots.TryGetValue(rootId, out var root))
        {
            throw new RootNotFoundException(rootId);
        }

        if (!root.IsWritable)
        {
            throw new PermissionDeniedException($"The root '{rootId}' is read-only.");
        }

        foreach (var path in relativePath.Split(';'))
        {
            if (!string.IsNullOrEmpty(path))
            {
                ValidateRelativePath(path);
            }
        }
        ValidateRelativePath(zipPath);

        logger.LogInformation("Starting compression for server {ServerId} root {RootId}", serverId, rootId);
        var taskId = backgroundOperationTracker.StartTracking(
            $"Compressing '{relativePath}' to '{zipPath}' in root '{rootId}'",
            ct => fileRepository.ArchiveAsync(root.Operation, relativePath, zipPath, ct));

        return Task.FromResult(taskId);
    }

    public Task<string> ExtractAsync(
        string serverId,
        string rootId,
        string zipPath,
        string destPath,
        CancellationToken cancellationToken)
    {
        if (!_options.Instances.TryGetValue(serverId, out var instance))
        {
            throw new InstanceNotFoundException(serverId);
        }

        if (!instance.Roots.TryGetValue(rootId, out var root))
        {
            throw new RootNotFoundException(rootId);
        }

        if (!root.IsWritable)
        {
            throw new PermissionDeniedException($"The root '{rootId}' is read-only.");
        }

        ValidateRelativePath(zipPath);
        ValidateRelativePath(destPath);

        logger.LogInformation("Starting extraction for server {ServerId} root {RootId}", serverId, rootId);
        var taskId = backgroundOperationTracker.StartTracking(
            $"Extracting '{zipPath}' to '{destPath}' in root '{rootId}'",
            ct => fileRepository.ExtractAsync(root.Operation, zipPath, destPath, ct));

        return Task.FromResult(taskId);
    }

    public TrackedOperationDto? GetOperationStatus(string taskId)
    {
        return backgroundOperationTracker.GetStatus(taskId);
    }

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

        if (normalized.StartsWith('/') || normalized.StartsWith('~') || normalized.Contains(':'))
        {
            throw new InvalidRelativePathException("Absolute path references are not permitted.");
        }
    }
}
