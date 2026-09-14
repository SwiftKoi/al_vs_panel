using AlegacyWebPanel.Modules.FileManager.Contracts;

namespace AlegacyWebPanel.Modules.FileManager.Services;

public interface IFileManagerService
{
    Task<DirectoryListingDto> GetDirectoryListingAsync(
        string serverId,
        string rootId,
        string relativePath,
        CancellationToken cancellationToken);

    Task DownloadFileAsync(
        string serverId,
        string rootId,
        string relativePath,
        Stream destination,
        CancellationToken cancellationToken);

    Task UploadFileAsync(
        string serverId,
        string rootId,
        string relativePath,
        Stream source,
        CancellationToken cancellationToken);

    Task<FileContentDto> GetTextContentAsync(
        string serverId,
        string rootId,
        string relativePath,
        CancellationToken cancellationToken);

    Task SaveTextContentAsync(
        string serverId,
        string rootId,
        string relativePath,
        string content,
        CancellationToken cancellationToken);

    Task CreateDirectoryAsync(
        string serverId,
        string rootId,
        string relativePath,
        CancellationToken cancellationToken);

    Task RenameAsync(
        string serverId,
        string rootId,
        string relativePath,
        string newName,
        CancellationToken cancellationToken);

    Task MoveAsync(
        string serverId,
        string rootId,
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken);

    Task DeleteAsync(
        string serverId,
        string rootId,
        string relativePath,
        CancellationToken cancellationToken);

    Task<string> ArchiveAsync(
        string serverId,
        string rootId,
        string relativePath,
        string zipPath,
        CancellationToken cancellationToken);

    Task<string> ExtractAsync(
        string serverId,
        string rootId,
        string zipPath,
        string destPath,
        CancellationToken cancellationToken);

    TrackedOperationDto? GetOperationStatus(string taskId);
}
