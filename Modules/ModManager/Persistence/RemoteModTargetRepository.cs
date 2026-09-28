using System.Text.Json;
using AlegacyWebPanel.Modules.ModManager.Exceptions;
using AlegacyWebPanel.Modules.RemoteOperations.Exceptions;
using AlegacyWebPanel.Modules.RemoteOperations.Services;

namespace AlegacyWebPanel.Modules.ModManager.Persistence;

/// <summary>Runs Server/*/mod-manager.py through RemoteOperations; see the helper for the exit-code contract.</summary>
public sealed class RemoteModTargetRepository(IRemoteOperationsService remoteOperations) : IModTargetRepository
{
    private const int ExitNotFound = 3;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<ModFileEntry>> ScanAsync(string operation, CancellationToken cancellationToken)
    {
        var entries = await RunJsonAsync<List<ScanEntry>>(operation, ["scan"], cancellationToken) ?? [];
        return entries.Select(entry => new ModFileEntry(
            entry.FileName,
            entry.Kind ?? "unknown",
            entry.SizeBytes,
            entry.ModifiedUtc,
            entry.ModInfo is null ? null : Convert.FromBase64String(entry.ModInfo),
            entry.Error)).ToArray();
    }

    public async Task<GameInfo> GetGameInfoAsync(string operation, CancellationToken cancellationToken) =>
        await RunJsonAsync<GameInfo>(operation, ["game-info"], cancellationToken)
        ?? new GameInfo(null, null, null);

    public async Task StageAsync(string operation, string batchId, string fileName, Stream content, CancellationToken cancellationToken)
    {
        try
        {
            await remoteOperations.ExecuteBinaryAsync(operation, ["stage", batchId, fileName], content, Stream.Null, cancellationToken);
        }
        catch (RemoteOperationFailedException exception)
        {
            throw new ModTargetException(HelperMessage(exception));
        }
    }

    public async Task<ModJournal> ApplyAsync(string operation, string batchId, IReadOnlyList<ModJournalItem> items, CancellationToken cancellationToken)
    {
        var arguments = new List<string> { "apply", batchId };
        foreach (var item in items)
        {
            arguments.Add(item.Old ?? "-");
            arguments.Add(item.New);
        }

        return await RunJsonAsync<ModJournal>(operation, arguments, cancellationToken)
               ?? throw new ModTargetException("The mod helper returned no result.");
    }

    public Task DiscardAsync(string operation, string batchId, CancellationToken cancellationToken) =>
        RunJsonAsync<JsonElement>(operation, ["discard", batchId], cancellationToken);

    public Task<ModJournal?> GetBackupAsync(string operation, CancellationToken cancellationToken) =>
        RunJsonAsync<ModJournal>(operation, ["backup"], cancellationToken);

    public async Task<ModJournal> RollbackAsync(string operation, CancellationToken cancellationToken)
    {
        try
        {
            return await RunJsonAsync<ModJournal>(operation, ["rollback"], cancellationToken)
                   ?? throw new ModTargetException("The mod helper returned no result.");
        }
        catch (RemoteOperationFailedException exception) when (exception.ExitStatus == ExitNotFound)
        {
            throw new ModBackupNotFoundException();
        }
    }

    private async Task<T?> RunJsonAsync<T>(string operation, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var result = await remoteOperations.ExecuteAsync(operation, arguments, cancellationToken);
        if (result.ExitStatus != 0)
        {
            var failure = new RemoteOperationFailedException(operation, result.ExitStatus, result.ErrorOutput);
            if (result.ExitStatus == ExitNotFound && arguments[0] == "rollback")
            {
                throw failure;
            }

            throw new ModTargetException(HelperMessage(failure));
        }

        try
        {
            return JsonSerializer.Deserialize<T>(result.StandardOutput, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new ModTargetException($"The mod helper returned invalid JSON: {exception.Message}");
        }
    }

    private static string HelperMessage(RemoteOperationFailedException exception)
    {
        var message = exception.ErrorOutput.Trim();
        return message.StartsWith("Error: ", StringComparison.Ordinal) ? message["Error: ".Length..] : message;
    }

    private sealed record ScanEntry(string FileName, string? Kind, long SizeBytes, DateTimeOffset ModifiedUtc, string? ModInfo, string? Error);
}
