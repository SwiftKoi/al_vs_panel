using System.IO;
using System.Text;
using System.Text.Json;
using AlegacyWebPanel.Modules.FileManager.Contracts;
using AlegacyWebPanel.Modules.RemoteOperations.Exceptions;
using AlegacyWebPanel.Modules.RemoteOperations.Services;

namespace AlegacyWebPanel.Modules.FileManager.Persistence;

public sealed class RemoteFileRepository(IRemoteOperationsService remoteOperationsService)
    : IFileRepository
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<IReadOnlyList<FileEntryDto>> ListAsync(
        string operation,
        string relativePath,
        CancellationToken cancellationToken)
    {
        using var stdout = new MemoryStream();

        await remoteOperationsService.ExecuteBinaryAsync(
            operation,
            ["list", relativePath],
            stdin: null,
            stdout: stdout,
            cancellationToken);

        stdout.Position = 0;
        using var reader = new StreamReader(stdout, Encoding.UTF8);
        var json = await reader.ReadToEndAsync(cancellationToken);

        try
        {
            var entries = JsonSerializer.Deserialize<List<FileEntryHelperModel>>(json, JsonSerializerOptions);
            return entries?.Select(e => new FileEntryDto(
                e.Name,
                e.IsFolder,
                e.Size,
                DateTimeOffset.Parse(e.Modified))).ToList() ?? [];
        }
        catch (JsonException exception)
        {
            throw new RemoteOperationFailedException(operation, 0, $"Failed to parse remote metadata JSON. Error: {exception.Message}. Raw output: {json}");
        }
        catch (FormatException exception)
        {
            throw new RemoteOperationFailedException(operation, 0, $"Failed to parse date format in metadata. Error: {exception.Message}. Raw output: {json}");
        }
    }

    public async Task ReadAsync(
        string operation,
        string relativePath,
        Stream destination,
        CancellationToken cancellationToken)
    {
        await remoteOperationsService.ExecuteBinaryAsync(
            operation,
            ["read", relativePath],
            stdin: null,
            stdout: destination,
            cancellationToken);
    }

    public async Task WriteAsync(
        string operation,
        string relativePath,
        Stream source,
        CancellationToken cancellationToken)
    {
        await remoteOperationsService.ExecuteBinaryAsync(
            operation,
            ["write", relativePath],
            stdin: source,
            stdout: null,
            cancellationToken);
    }

    public async Task CreateDirectoryAsync(
        string operation,
        string relativePath,
        CancellationToken cancellationToken)
    {
        await remoteOperationsService.ExecuteBinaryAsync(
            operation,
            ["mkdir", relativePath],
            stdin: null,
            stdout: null,
            cancellationToken);
    }

    public async Task RenameAsync(
        string operation,
        string relativePath,
        string newName,
        CancellationToken cancellationToken)
    {
        await remoteOperationsService.ExecuteBinaryAsync(
            operation,
            ["rename", relativePath, newName],
            stdin: null,
            stdout: null,
            cancellationToken);
    }

    public async Task MoveAsync(
        string operation,
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        await remoteOperationsService.ExecuteBinaryAsync(
            operation,
            ["move", sourcePath, destinationPath],
            stdin: null,
            stdout: null,
            cancellationToken);
    }

    public async Task DeleteAsync(
        string operation,
        string relativePath,
        CancellationToken cancellationToken)
    {
        await remoteOperationsService.ExecuteBinaryAsync(
            operation,
            ["delete", relativePath],
            stdin: null,
            stdout: null,
            cancellationToken);
    }

    public async Task ArchiveAsync(
        string operation,
        string relativePath,
        string zipPath,
        CancellationToken cancellationToken)
    {
        await remoteOperationsService.ExecuteBinaryAsync(
            operation,
            ["zip", relativePath, zipPath],
            stdin: null,
            stdout: null,
            cancellationToken);
    }

    public async Task ExtractAsync(
        string operation,
        string zipPath,
        string destPath,
        CancellationToken cancellationToken)
    {
        await remoteOperationsService.ExecuteBinaryAsync(
            operation,
            ["unzip", zipPath, destPath],
            stdin: null,
            stdout: null,
            cancellationToken);
    }

    private sealed class FileEntryHelperModel
    {
        public string Name { get; set; } = string.Empty;
        public bool IsFolder { get; set; }
        public long Size { get; set; }
        public string Modified { get; set; } = string.Empty;
    }
}
