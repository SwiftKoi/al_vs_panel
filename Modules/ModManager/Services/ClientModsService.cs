using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using AlegacyWebPanel.Modules.ModManager.Configuration;
using AlegacyWebPanel.Modules.ModManager.Exceptions;
using AlegacyWebPanel.Modules.ModManager.Persistence;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace AlegacyWebPanel.Modules.ModManager.Services;

/// <summary>Only one archive is built at a time; requests for the same version wait for it.</summary>
public sealed class ClientModsGate
{
    public SemaphoreSlim Build { get; } = new(1, 1);
}

public sealed class ClientModsService(
    IModTargetRepository target,
    IMemoryCache cache,
    ClientModsGate gate,
    IOptions<ModManagerOptions> options,
    TimeProvider timeProvider) : IClientModsService
{
    private const int MaximumFailures = 5;
    private static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(10);
    // The launcher sends one request per run of changed mods, each would otherwise start a helper over SSH.
    private static readonly TimeSpan PasswordMemory = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ScanMemory = TimeSpan.FromSeconds(30);

    public static IEnumerable<ModFileEntry> SelectClientMods(IEnumerable<ModFileEntry> files) =>
        files.Where(IsClientMod).OrderBy(file => file.FileName, StringComparer.Ordinal);

    /// <summary>A file without a readable modinfo stays out: nothing says players may have it.</summary>
    private static bool IsClientMod(ModFileEntry file)
    {
        if (file.ModInfo is null ||
            !file.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
            ModInfoParser.TryParse(file.ModInfo) is not { } info)
        {
            return false;
        }

        return info.Side != "server" && info.RequiredOnClient != false;
    }

    public async Task<ClientModsAccess> CheckPasswordAsync(
        string serverId,
        string password,
        string client,
        CancellationToken cancellationToken)
    {
        var operation = GetOperation(serverId);
        // A proven password goes first: someone else's mistakes from a shared address must not stop a download.
        var passwordKey = $"client-mods:password:{serverId}:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(password)))}";
        if (cache.TryGetValue(passwordKey, out _))
        {
            return ClientModsAccess.Allowed;
        }

        var failuresKey = $"client-mods:failures:{client}";
        if (cache.Get<int>(failuresKey) >= MaximumFailures)
        {
            return ClientModsAccess.Blocked;
        }

        if (password.Length > 0 && await target.CheckServerPasswordAsync(operation, password, cancellationToken))
        {
            cache.Set(passwordKey, true, PasswordMemory);
            cache.Remove(failuresKey);
            return ClientModsAccess.Allowed;
        }

        cache.Set(failuresKey, cache.Get<int>(failuresKey) + 1, FailureWindow);
        return ClientModsAccess.WrongPassword;
    }

    public async Task<ClientModArchive> GetArchiveAsync(string serverId, CancellationToken cancellationToken)
    {
        var operation = GetOperation(serverId);
        var mods = await cache.GetOrCreateAsync($"client-mods:scan:{serverId}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = ScanMemory;
            return SelectClientMods(await target.ScanAsync(operation, cancellationToken)).ToArray();
        }) ?? [];

        var fingerprint = Fingerprint(mods);
        var directory = options.Value.ClientModsDirectory;
        var path = Path.Combine(directory, $"{serverId}-{fingerprint}.zip");
        if (!File.Exists(path))
        {
            await gate.Build.WaitAsync(cancellationToken);
            try
            {
                if (!File.Exists(path))
                {
                    await BuildAsync(operation, mods, directory, path, cancellationToken);
                    RemoveOlderArchives(directory, serverId, path);
                }
            }
            finally
            {
                gate.Build.Release();
            }
        }

        return new ClientModArchive(path, fingerprint, new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero));
    }

    public static string Fingerprint(IEnumerable<ModFileEntry> mods)
    {
        var text = string.Join('\n', mods.Select(mod => $"{mod.FileName}|{mod.SizeBytes}|{mod.ModifiedUtc.UtcTicks}"));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];
    }

    /// <summary>
    /// Mods are stored without compression: the launcher takes size and checksum of each from the central
    /// directory and downloads only the changed ones. A failed build leaves nothing under the final name.
    /// </summary>
    private async Task BuildAsync(
        string operation,
        IReadOnlyList<ModFileEntry> mods,
        string directory,
        string path,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        var temporary = $"{path}.{Guid.NewGuid():N}.part";
        try
        {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var mod in mods)
                {
                    var entry = archive.CreateEntry(mod.FileName, CompressionLevel.NoCompression);
                    entry.LastWriteTime = mod.ModifiedUtc.Year >= 1980 ? mod.ModifiedUtc : timeProvider.GetUtcNow();
                    await using var content = entry.Open();
                    await target.ReadFileAsync(operation, mod.FileName, content, cancellationToken);
                }
            }

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    /// <summary>Downloads already running keep their open file, so the old version can go at once.</summary>
    private static void RemoveOlderArchives(string directory, string serverId, string current)
    {
        foreach (var old in Directory.EnumerateFiles(directory, $"{serverId}-*.zip"))
        {
            if (old != current)
            {
                File.Delete(old);
            }
        }
    }

    private string GetOperation(string serverId) =>
        options.Value.Servers.TryGetValue(serverId, out var server) &&
        server.ClientMods &&
        !string.IsNullOrWhiteSpace(server.Operation)
            ? server.Operation
            : throw new ClientModsNotEnabledException(serverId);
}
