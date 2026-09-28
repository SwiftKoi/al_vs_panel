using AlegacyWebPanel.Modules.FileManager.Contracts;

namespace AlegacyWebPanel.Modules.FileManager.Persistence;

public sealed record DirectoryEntries(
    IReadOnlyList<FileEntryDto> Entries,
    bool Truncated,
    int Skipped);

public interface IFileRepository
{
    Task<DirectoryEntries> ListAsync(
        string operation,
        string relativePath,
        int maximumEntries,
        CancellationToken cancellationToken);

    /// <summary>Case-insensitive name search below a folder; stops at the limit or a time budget.</summary>
    Task<SearchResponseDto> SearchAsync(
        string operation,
        string relativePath,
        string query,
        bool recursive,
        int maximumResults,
        CancellationToken cancellationToken);

    /// <summary>Total size of a folder or file (time-limited; <c>Complete</c> is false when cut short).</summary>
    Task<FolderSizeDto> MeasureAsync(string operation, string relativePath, CancellationToken cancellationToken);

    /// <summary>Metadata of one entry (symlinks followed), or null when it does not exist.</summary>
    Task<FileEntryDto?> StatAsync(
        string operation,
        string relativePath,
        CancellationToken cancellationToken);

    Task ReadAsync(
        string operation,
        string relativePath,
        Stream destination,
        CancellationToken cancellationToken);

    Task WriteAsync(
        string operation,
        string relativePath,
        Stream source,
        CancellationToken cancellationToken,
        bool createParents = false);

    /// <summary>Builds <c>.panel-tmp/&lt;archiveId&gt;.zip</c> from the sources, for a one-off download.</summary>
    Task ArchiveForDownloadAsync(
        string operation,
        string archiveId,
        IReadOnlyList<string> sourcePaths,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken);

    Task DeleteDownloadArchiveAsync(string operation, string archiveId, CancellationToken cancellationToken);

    Task CreateDirectoryAsync(
        string operation,
        string relativePath,
        CancellationToken cancellationToken);

    Task RenameAsync(
        string operation,
        string relativePath,
        string newName,
        CancellationToken cancellationToken);

    Task MoveAsync(
        string operation,
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken);

    Task DeleteAsync(
        string operation,
        string relativePath,
        CancellationToken cancellationToken);

    /// <summary>Moves an entry into the root's trash; expired trash is purged first.</summary>
    Task<TrashEntryDto> TrashAsync(string operation, string relativePath, int retentionDays, CancellationToken cancellationToken);

    Task<IReadOnlyList<TrashEntryDto>> ListTrashAsync(string operation, int retentionDays, CancellationToken cancellationToken);

    /// <summary>Moves a trashed entry back to its original path; fails if that path is taken.</summary>
    Task<TrashEntryDto> RestoreTrashAsync(string operation, string trashId, CancellationToken cancellationToken);

    /// <summary>Permanently removes one trash entry, or all of them when <paramref name="trashId"/> is null.</summary>
    Task PurgeTrashAsync(string operation, string? trashId, CancellationToken cancellationToken);

    Task ArchiveAsync(
        string operation,
        IReadOnlyList<string> sourcePaths,
        string zipPath,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken);

    Task ExtractAsync(
        string operation,
        string zipPath,
        string destPath,
        long maximumExtractedBytes,
        int maximumEntries,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken);
}
