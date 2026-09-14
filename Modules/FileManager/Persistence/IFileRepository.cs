using AlegacyWebPanel.Modules.FileManager.Contracts;

namespace AlegacyWebPanel.Modules.FileManager.Persistence;

public interface IFileRepository
{
    Task<IReadOnlyList<FileEntryDto>> ListAsync(
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
        string relativePath,
        string zipPath,
        CancellationToken cancellationToken);

    Task ExtractAsync(
        string operation,
        string zipPath,
        string destPath,
        CancellationToken cancellationToken);
}
