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

    public async Task<SearchResponseDto> SearchAsync(
        string operation,
        string relativePath,
        string query,
        bool recursive,
        int maximumResults,
        CancellationToken cancellationToken)
    {
        var json = await ExecuteForTextAsync(
            operation,
            ["find", relativePath, query, recursive ? "1" : "0", maximumResults.ToString(CultureInfo.InvariantCulture)],
            cancellationToken);

        var model = Parse<SearchHelperModel>(operation, json);
        var results = model?.Results
            .Select(entry =>
            {
                var dto = ToDto(operation, entry, json);
                return new SearchResultDto(entry.Path, dto.Name, dto.IsFolder, dto.Size, dto.Modified);
            })
            .ToList() ?? [];
        return new SearchResponseDto(results, model?.Truncated ?? false);
    }

    public async Task<FolderSizeDto> MeasureAsync(string operation, string relativePath, CancellationToken cancellationToken)
    {
        var json = await ExecuteForTextAsync(operation, ["size", relativePath], cancellationToken);
        return Parse<FolderSizeDto>(operation, json)
            ?? throw new RemoteOperationFailedException(operation, 0, $"Failed to parse size output. Raw output: {json}");
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
        CancellationToken cancellationToken,
        bool createParents = false) =>
        ExecuteAsync(operation, ["write", relativePath, createParents ? "1" : "0"], source, null, cancellationToken);

    public Task ArchiveForDownloadAsync(
        string operation,
        string archiveId,
        IReadOnlyList<string> sourcePaths,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken) =>
        ExecuteWithProgressAsync(operation, ["zip-tmp", archiveId, .. sourcePaths], progress, cancellationToken);

    public Task DeleteDownloadArchiveAsync(string operation, string archiveId, CancellationToken cancellationToken) =>
        ExecuteAsync(operation, ["tmp-delete", archiveId], null, null, cancellationToken);

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

    public async Task<TrashEntryDto> TrashAsync(string operation, string relativePath, int retentionDays, CancellationToken cancellationToken)
    {
        var json = await ExecuteForTextAsync(
            operation, ["trash", relativePath, retentionDays.ToString(CultureInfo.InvariantCulture)], cancellationToken);
        return ToTrashDto(operation, Parse<TrashHelperModel>(operation, json), json);
    }

    public async Task<IReadOnlyList<TrashEntryDto>> ListTrashAsync(string operation, int retentionDays, CancellationToken cancellationToken)
    {
        var json = await ExecuteForTextAsync(
            operation, ["trash-list", retentionDays.ToString(CultureInfo.InvariantCulture)], cancellationToken);
        return Parse<List<TrashHelperModel>>(operation, json)?.Select(m => ToTrashDto(operation, m, json)).ToList() ?? [];
    }

    public async Task<TrashEntryDto> RestoreTrashAsync(string operation, string trashId, CancellationToken cancellationToken)
    {
        var json = await ExecuteForTextAsync(operation, ["trash-restore", trashId], cancellationToken);
        return ToTrashDto(operation, Parse<TrashHelperModel>(operation, json), json);
    }

    public Task PurgeTrashAsync(string operation, string? trashId, CancellationToken cancellationToken) =>
        ExecuteAsync(operation, ["trash-purge", trashId ?? "*"], null, null, cancellationToken);

    private static TrashEntryDto ToTrashDto(string operation, TrashHelperModel? model, string json)
    {
        if (model is null || !DateTimeOffset.TryParse(model.DeletedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var deletedAt))
        {
            throw new RemoteOperationFailedException(operation, 0, $"Failed to parse trash metadata. Raw output: {json}");
        }

        return new TrashEntryDto(model.Id, model.OriginalPath, model.Name, model.IsFolder, model.Size, deletedAt);
    }

    public Task ArchiveAsync(
        string operation,
        IReadOnlyList<string> sourcePaths,
        string zipPath,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken) =>
        ExecuteWithProgressAsync(operation, ["zip", zipPath, .. sourcePaths], progress, cancellationToken);

    public Task ExtractAsync(
        string operation,
        string zipPath,
        string destPath,
        long maximumExtractedBytes,
        int maximumEntries,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken) =>
        ExecuteWithProgressAsync(
            operation,
            [
                "unzip",
                zipPath,
                destPath,
                maximumExtractedBytes.ToString(CultureInfo.InvariantCulture),
                maximumEntries.ToString(CultureInfo.InvariantCulture)
            ],
            progress,
            cancellationToken);

    private async Task ExecuteWithProgressAsync(
        string operation,
        IReadOnlyList<string> arguments,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        // The helper prints one JSON progress line on stdout at a time.
        await using var stdout = new ProgressLineStream(progress);
        await ExecuteAsync(operation, arguments, null, stdout, cancellationToken);
    }

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

    private sealed class TrashHelperModel
    {
        public string Id { get; set; } = string.Empty;
        public string OriginalPath { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsFolder { get; set; }
        public long Size { get; set; }
        public string DeletedAt { get; set; } = string.Empty;
    }

    private sealed class SearchHelperModel
    {
        public List<FileEntryHelperModel> Results { get; set; } = [];
        public bool Truncated { get; set; }
    }

    private class FileEntryHelperModel
    {
        public string Path { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsFolder { get; set; }
        public long Size { get; set; }
        public string Modified { get; set; } = string.Empty;
    }

    /// <summary>Write-only sink that parses newline-delimited progress JSON from the helper.</summary>
    private sealed class ProgressLineStream(IProgress<OperationProgress>? progress) : Stream
    {
        private readonly List<byte> _line = [];

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            foreach (var b in buffer)
            {
                if (b != (byte)'\n')
                {
                    // Guard against a helper that never sends a newline.
                    if (_line.Count < 4096) _line.Add(b);
                    continue;
                }

                Report();
                _line.Clear();
            }
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Write(buffer.AsSpan(offset, count));
            return Task.CompletedTask;
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Write(buffer.Span);
            return ValueTask.CompletedTask;
        }

        private void Report()
        {
            if (progress is null || _line.Count == 0) return;
            try
            {
                var model = JsonSerializer.Deserialize<ProgressHelperModel>(_line.ToArray(), JsonSerializerOptions);
                if (model is not null)
                {
                    progress.Report(new OperationProgress(model.Items, model.TotalItems, model.Bytes, model.TotalBytes));
                }
            }
            catch (JsonException)
            {
                // Progress is best-effort; an unparsable line is ignored.
            }
        }
    }

    private sealed class ProgressHelperModel
    {
        public long Items { get; set; }
        public long TotalItems { get; set; }
        public long Bytes { get; set; }
        public long TotalBytes { get; set; }
    }
}
