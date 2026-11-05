namespace AlegacyWebPanel.Modules.ModManager.Services;

public enum ClientModsAccess
{
    Allowed,
    WrongPassword,
    Blocked
}

public sealed record ClientModArchive(string Path, string Fingerprint, DateTimeOffset ModifiedUtc);

/// <summary>The mods a player needs to join a server, as one archive the launcher reads in parts.</summary>
public interface IClientModsService
{
    Task<ClientModsAccess> CheckPasswordAsync(string serverId, string password, string client, CancellationToken cancellationToken);
    Task<ClientModArchive> GetArchiveAsync(string serverId, CancellationToken cancellationToken);
}
