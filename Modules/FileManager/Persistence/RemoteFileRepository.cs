using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using AlegacyWebPanel.Modules.FileManager.Contracts;
using AlegacyWebPanel.Modules.FileManager.Exceptions;
using AlegacyWebPanel.Modules.RemoteOperations.Exceptions;
using AlegacyWebPanel.Modules.RemoteOperations.Services;

namespace AlegacyWebPanel.Modules.FileManager.Persistence;

public sealed class RemoteFileRepository(IRemoteOperationsService remoteOperationsService)
    : IFileRepository
{
    // Exit codes of file-manager.py (see its module docstring).
    private const int ExitNotPermitted = 2;
    private const int ExitNotFound = 3;
    private const int ExitWrongType = 4;
    private const int ExitExists = 17;
    private const int ExitLimit = 18;

    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<DirectoryEntries> ListAsync(
        string operation,
        string relativePath,
        int maximumEntries,
        CancellationToken cancellationToken)
    {
        var json = await ExecuteForTextAsync(
            operation,
            ["list", relativePath, maximumEntries.ToString(CultureInfo.InvariantCulture)],
            cancellationToken);

        var listing = Parse<ListingHelperModel>(operation, json);
        return new DirectoryEntries(
            listing?.Entries.Select(entry => ToDto(operation, entry, json)).ToList() ?? [],
            listing?.Truncated ?? false,
            listing?.Skipped ?? 0);
    }

    public async Task<FileEntryDto?> StatAsync(
        string operation,
        string relativePath,
        CancellationToken cancellationToken)
    {
        try
        {
            var json = await ExecuteForTextAsync(operation, ["stat", relativePath], cancellationToken);
            var entry = Parse<FileEntryHelperModel>(operation, json);
            return entry is null ? null : ToDto(operation, entry, json);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    public Task ReadAsync(
        string operation,
        string relativePath,
        Stream destination,
        CancellationToken cancellationToken) =>
        ExecuteAsync(operation, ["read", relativePath], null, destination, cancellationToken);

    public Task WriteAsync(
        string operation,
        string relativePath,
        Stream source,
        CancellationToken cancellationToken) =>
        ExecuteAsync(operation, ["write", relativePath], source, null, cancellationToken);

    public Task CreateDirectoryAsync(
        string operation,
        string relativePath,
        CancellationToken cancellationToken) =>
        ExecuteAsync(operation, ["mkdir", relativePath], null, null, cancellationToken);

    public Task RenameAsync(
        string operation,
        string relativePath,
        string newName,
        CancellationToken cancellationToken) =>
        ExecuteAsync(operation, ["rename", relativePath, newName], null, null, cancellationToken);

    public Task MoveAsync(
        string operation,
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken) =>
        ExecuteAsync(operation, ["move", sourcePath, destinationPath], null, null, cancellationToken);

    public Task DeleteAsync(
        string operation,
        string relativePath,
        CancellationToken cancellationToken) =>
        ExecuteAsync(operation, ["delete", relativePath], null, null, cancellationToken);

    public Task ArchiveAsync(
        string operation,
        IReadOnlyList<string> sourcePaths,
        string zipPath,
        CancellationToken cancellationToken) =>
        ExecuteAsync(operation, ["zip", zipPath, .. sourcePaths], null, null, cancellationToken);

    public Task ExtractAsync(
        string operation,
        string zipPath,
        string destPath,
        long maximumExtractedBytes,
        int maximumEntries,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            operation,
            [
                "unzip",
                zipPath,
                destPath,
                maximumExtractedBytes.ToString(CultureInfo.InvariantCulture),
                maximumEntries.ToString(CultureInfo.InvariantCulture)
            ],
            null,
            null,
            cancellationToken);

    private async Task<string> ExecuteForTextAsync(
        string operation,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        using var stdout = new MemoryStream();
        await ExecuteAsync(operation, arguments, null, stdout, cancellationToken);
        return Encoding.UTF8.GetString(stdout.GetBuffer(), 0, (int)stdout.Length);
    }

    private async Task ExecuteAsync(
        string operation,
        IReadOnlyList<string> arguments,
        Stream? stdin,
        Stream? stdout,
        CancellationToken cancellationToken)
    {
        try
        {
            await remoteOperationsService.ExecuteBinaryAsync(operation, arguments, stdin, stdout, cancellationToken);
        }
        catch (RemoteOperationFailedException exception)
        {
            // The helper reports expected outcomes through its exit code; give those
            // their own domain errors instead of a generic "remote operation failed".
            var message = HelperMessage(exception.ErrorOutput);
            throw exception.ExitStatus switch
            {
                ExitNotPermitted => new InvalidRelativePathException(message),
                ExitNotFound => new FileNotFoundException(message),
                ExitWrongType => new InvalidFileOperationException(message),
                ExitExists => new ItemAlreadyExistsException(message),
                ExitLimit => new FileTooLargeException(message),
                _ => exception
            };
        }
    }

    private static string HelperMessage(string errorOutput)
    {
        var message = errorOutput.Trim();
        return message.StartsWith("Error: ", StringComparison.Ordinal) ? message["Error: ".Length..] : message;
    }

    private static T? Parse<T>(string operation, string json)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonSerializerOptions);
        }
        catch (JsonException exception)
        {
            throw new RemoteOperationFailedException(operation, 0, $"Failed to parse remote metadata JSON. Error: {exception.Message}. Raw output: {json}");
        }
    }

    private static FileEntryDto ToDto(string operation, FileEntryHelperModel entry, string json)
    {
        if (!DateTimeOffset.TryParse(entry.Modified, CultureInfo.InvariantCulture, DateTimeStyles.None, out var modified))
        {
            throw new RemoteOperationFailedException(operation, 0, $"Failed to parse date format in metadata. Raw output: {json}");
        }

        return new FileEntryDto(entry.Name, entry.IsFolder, entry.Size, modified);
    }

    private sealed class ListingHelperModel
    {
        public List<FileEntryHelperModel> Entries { get; set; } = [];
        public bool Truncated { get; set; }
        public int Skipped { get; set; }
    }

    private sealed class FileEntryHelperModel
    {
        public string Name { get; set; } = string.Empty;
        public bool IsFolder { get; set; }
        public long Size { get; set; }
        public string Modified { get; set; } = string.Empty;
    }
}
