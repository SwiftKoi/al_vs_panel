namespace AlegacyWebPanel.Modules.ModManager.Persistence;

public interface IModDbRepository
{
    Task<ModDbLookup> GetModAsync(string modId, bool bypassCache, CancellationToken cancellationToken);

    /// <summary>Downloads a release file into <paramref name="destination"/>, enforcing the trusted-host and size policy.</summary>
    Task DownloadAsync(string url, Stream destination, CancellationToken cancellationToken);
}
