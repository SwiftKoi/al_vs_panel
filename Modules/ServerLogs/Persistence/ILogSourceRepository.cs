namespace AlegacyWebPanel.Modules.ServerLogs.Persistence;

public sealed record LogFileInfo(string Path, long Size, DateTimeOffset ModifiedUtc, string? Identity);

/// <summary>Read-only access to the game server's log files (Server/*/server-logs.py).</summary>
public interface ILogSourceRepository
{
    Task<IReadOnlyList<LogFileInfo>> ListAsync(string operation, CancellationToken cancellationToken);

    /// <summary>Bytes from <paramref name="offset"/>, ending at the last complete line; empty when no complete line is available.</summary>
    Task<byte[]> ReadAsync(string operation, string path, long offset, int maximumBytes, CancellationToken cancellationToken);
}
