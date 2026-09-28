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
        CancellationToken cancellationToken);

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

    Task ArchiveAsync(
        string operation,
        IReadOnlyList<string> sourcePaths,
        string zipPath,
        CancellationToken cancellationToken);

    Task ExtractAsync(
        string operation,
        string zipPath,
        string destPath,
        long maximumExtractedBytes,
        int maximumEntries,
        CancellationToken cancellationToken);
}
