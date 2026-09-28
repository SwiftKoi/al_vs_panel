namespace AlegacyWebPanel.Modules.ModManager.Persistence;

public sealed record ModFileEntry(
    string FileName,
    string Kind,
    long SizeBytes,
    DateTimeOffset ModifiedUtc,
    byte[]? ModInfo,
    string? Error);

public sealed record GameInfo(string? GameVersion, IReadOnlyList<string>? LoadedMods, DateTimeOffset? LogModifiedUtc);

public sealed record ModJournalItem(string? Old, string New);

public sealed record ModJournal(string BatchId, DateTimeOffset AppliedUtc, IReadOnlyList<ModJournalItem> Items);

/// <summary>The game server's Mods folder, reached through the target-side mod-manager.py helper.</summary>
public interface IModTargetRepository
{
    Task<IReadOnlyList<ModFileEntry>> ScanAsync(string operation, CancellationToken cancellationToken);
    Task<GameInfo> GetGameInfoAsync(string operation, CancellationToken cancellationToken);
    Task StageAsync(string operation, string batchId, string fileName, Stream content, CancellationToken cancellationToken);
    Task<ModJournal> ApplyAsync(string operation, string batchId, IReadOnlyList<ModJournalItem> items, CancellationToken cancellationToken);
    Task DiscardAsync(string operation, string batchId, CancellationToken cancellationToken);
    Task<ModJournal?> GetBackupAsync(string operation, CancellationToken cancellationToken);
    Task<ModJournal> RollbackAsync(string operation, CancellationToken cancellationToken);
}
