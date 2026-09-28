using AlegacyWebPanel.Modules.FileManager.Contracts;

namespace AlegacyWebPanel.Modules.FileManager.Services;

public interface IFileManagerService
{
    IReadOnlyList<FileRootDto> GetRoots(string serverId);

    /// <summary>Finds entries whose name contains <paramref name="query"/> (case-insensitive) in a folder, optionally including subfolders.</summary>
    Task<SearchResponseDto> SearchAsync(
        string serverId,
        string rootId,
        string relativePath,
        string query,
        bool recursive,
        CancellationToken cancellationToken);

    Task<FolderSizeDto> MeasureAsync(string serverId, string rootId, string relativePath, CancellationToken cancellationToken);

    /// <summary>Metadata of a file; throws when it is missing or a folder. Call before streaming a download.</summary>
    Task<FileEntryDto> GetFileInfoAsync(
        string serverId,
        string rootId,
        string relativePath,
        CancellationToken cancellationToken);

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

    /// <param name="createFolders">Create missing parent folders (folder uploads).</param>
    Task UploadFileAsync(
        string serverId,
        string rootId,
        string relativePath,
        Stream source,
        CancellationToken cancellationToken,
        bool createFolders = false);

    /// <summary>Starts zipping several items into a temporary archive for download; poll the task, then call <see cref="DownloadArchiveAsync"/>.</summary>
    Task<DownloadArchiveDto> PrepareDownloadArchiveAsync(
        string serverId,
        string rootId,
        IReadOnlyList<string> sourcePaths,
        CancellationToken cancellationToken);

    /// <summary>Streams a prepared archive and deletes it afterwards.</summary>
    Task DownloadArchiveAsync(
        string serverId,
        string rootId,
        string archiveId,
        Stream destination,
        CancellationToken cancellationToken);

    /// <summary>Checks a prepared archive exists before streaming it.</summary>
    Task EnsureDownloadArchiveAsync(string serverId, string rootId, string archiveId, CancellationToken cancellationToken);

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
        DateTimeOffset? expectedModified,
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

    /// <summary>Moves the entry to the root's trash, or removes it for good when <paramref name="permanent"/> is set.</summary>
    Task<TrashEntryDto?> DeleteAsync(
        string serverId,
        string rootId,
        string relativePath,
        bool permanent,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<TrashEntryDto>> ListTrashAsync(string serverId, string rootId, CancellationToken cancellationToken);

    Task<TrashEntryDto> RestoreTrashAsync(string serverId, string rootId, string trashId, CancellationToken cancellationToken);

    /// <summary>Permanently removes one trash entry, or empties the trash when <paramref name="trashId"/> is null.</summary>
    Task PurgeTrashAsync(string serverId, string rootId, string? trashId, CancellationToken cancellationToken);

    Task<string> ArchiveAsync(
        string serverId,
        string rootId,
        IReadOnlyList<string> sourcePaths,
        string zipPath,
        CancellationToken cancellationToken);

    Task<string> ExtractAsync(
        string serverId,
        string rootId,
        string zipPath,
        string destPath,
        CancellationToken cancellationToken);

    TrackedOperationDto? GetOperationStatus(string taskId);

    IReadOnlyList<TrackedOperationDto> GetRecentOperations();

    /// <summary>Requests cancellation; false when the task is unknown or already finished.</summary>
    bool CancelOperation(string taskId);
}
