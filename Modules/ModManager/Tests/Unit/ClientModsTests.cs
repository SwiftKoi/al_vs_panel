using System.IO.Compression;
using System.Text;
using AlegacyWebPanel.Modules.ModManager.Configuration;
using AlegacyWebPanel.Modules.ModManager.Exceptions;
using AlegacyWebPanel.Modules.ModManager.Persistence;
using AlegacyWebPanel.Modules.ModManager.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace AlegacyWebPanel.Modules.ModManager.Tests.Unit;

public sealed class ClientModsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"client-mods-{Guid.NewGuid():N}");
    private readonly FakeTarget _target = new();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    public void Dispose()
    {
        _cache.Dispose();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private ClientModsService Service(bool enabled = true) => new(
        _target,
        _cache,
        new ClientModsGate(),
        Options.Create(new ModManagerOptions
        {
            ClientModsDirectory = _directory,
            Servers = { ["test"] = new ModManagerServerOptions { Operation = "test-mods", ClientMods = enabled } }
        }),
        TimeProvider.System);

    private static ModFileEntry Mod(string fileName, string modInfo, long size = 3, string kind = "zip") =>
        new(fileName, kind, size, new DateTimeOffset(2026, 11, 3, 10, 0, 0, TimeSpan.Zero), Encoding.UTF8.GetBytes(modInfo), null);

    [Fact]
    public void Server_side_and_optional_mods_stay_out()
    {
        ModFileEntry[] files =
        [
            Mod("universal.zip", """{ modid: "a", side: "Universal", requiredOnClient: true }"""),
            Mod("noside.zip", """{ "modid": "b" }"""),
            Mod("server.zip", """{ "modid": "c", "side": "Server", "requiredOnClient": false }"""),
            Mod("lower.zip", """{ "modid": "d", "side": "server" }"""),
            Mod("optional.zip", """{ "modid": "e", "side": "universal", "requiredOnClient": false }"""),
            Mod("broken.zip", "not json"),
            new ModFileEntry("nomodinfo.zip", "zip", 3, DateTimeOffset.UnixEpoch, null, null),
            Mod("code.cs", """{ "modid": "f" }""", kind: "code")
        ];

        var names = ClientModsService.SelectClientMods(files).Select(file => file.FileName);

        Assert.Equal(["noside.zip", "universal.zip"], names);
    }

    [Fact]
    public async Task Archive_stores_mods_without_compression()
    {
        _target.Files = [Mod("a b.zip", """{ "modid": "a" }"""), Mod("server.zip", """{ "modid": "c", "side": "server" }""")];

        var archive = await Service().GetArchiveAsync("test", CancellationToken.None);

        using var zip = ZipFile.OpenRead(archive.Path);
        var entry = Assert.Single(zip.Entries);
        Assert.Equal("a b.zip", entry.Name);
        Assert.Equal(entry.Length, entry.CompressedLength);
        await using var content = entry.Open();
        using var reader = new StreamReader(content);
        Assert.Equal("a b.zip", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task Changed_mod_replaces_the_archive_and_unchanged_one_is_reused()
    {
        _target.Files = [Mod("a.zip", """{ "modid": "a" }""")];
        var first = await Service().GetArchiveAsync("test", CancellationToken.None);
        var again = await Service().GetArchiveAsync("test", CancellationToken.None);
        Assert.Equal(first.Path, again.Path);
        Assert.Equal(1, _target.Reads);

        _cache.Clear();
        _target.Files = [Mod("a.zip", """{ "modid": "a" }""", size: 9)];
        var changed = await Service().GetArchiveAsync("test", CancellationToken.None);

        Assert.NotEqual(first.Path, changed.Path);
        Assert.False(File.Exists(first.Path));
        Assert.Equal(2, _target.Reads);
    }

    [Fact]
    public async Task Failed_build_leaves_no_archive()
    {
        _target.Files = [Mod("a.zip", """{ "modid": "a" }""")];
        _target.FailReads = true;

        await Assert.ThrowsAsync<ModTargetException>(() => Service().GetArchiveAsync("test", CancellationToken.None));

        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task Server_without_the_setting_offers_nothing()
    {
        var service = Service(enabled: false);

        await Assert.ThrowsAsync<ClientModsNotEnabledException>(() =>
            service.CheckPasswordAsync("test", "right", "1.1.1.1", CancellationToken.None));
        await Assert.ThrowsAsync<ClientModsNotEnabledException>(() =>
            service.GetArchiveAsync("test", CancellationToken.None));
        await Assert.ThrowsAsync<ClientModsNotEnabledException>(() =>
            service.GetArchiveAsync("unknown", CancellationToken.None));
    }

    [Fact]
    public async Task Right_password_is_remembered_so_the_target_is_asked_once()
    {
        var service = Service();

        Assert.Equal(ClientModsAccess.Allowed, await service.CheckPasswordAsync("test", "right", "1.1.1.1", CancellationToken.None));
        Assert.Equal(ClientModsAccess.Allowed, await service.CheckPasswordAsync("test", "right", "1.1.1.1", CancellationToken.None));

        Assert.Equal(1, _target.PasswordChecks);
    }

    [Fact]
    public async Task Empty_password_never_reaches_the_target()
    {
        Assert.Equal(ClientModsAccess.WrongPassword, await Service().CheckPasswordAsync("test", "", "1.1.1.1", CancellationToken.None));
        Assert.Equal(0, _target.PasswordChecks);
    }

    [Fact]
    public async Task Five_wrong_passwords_block_that_address_only()
    {
        var service = Service();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            Assert.Equal(ClientModsAccess.WrongPassword, await service.CheckPasswordAsync("test", "wrong", "1.1.1.1", CancellationToken.None));
        }

        Assert.Equal(ClientModsAccess.Blocked, await service.CheckPasswordAsync("test", "right", "1.1.1.1", CancellationToken.None));
        Assert.Equal(5, _target.PasswordChecks);
        Assert.Equal(ClientModsAccess.Allowed, await service.CheckPasswordAsync("test", "right", "2.2.2.2", CancellationToken.None));
    }

    [Fact]
    public async Task Proven_password_still_works_from_a_blocked_address()
    {
        var service = Service();
        await service.CheckPasswordAsync("test", "right", "1.1.1.1", CancellationToken.None);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await service.CheckPasswordAsync("test", "wrong", "1.1.1.1", CancellationToken.None);
        }

        Assert.Equal(ClientModsAccess.Blocked, await service.CheckPasswordAsync("test", "wrong", "1.1.1.1", CancellationToken.None));
        Assert.Equal(ClientModsAccess.Allowed, await service.CheckPasswordAsync("test", "right", "1.1.1.1", CancellationToken.None));
    }

    private sealed class FakeTarget : IModTargetRepository
    {
        public IReadOnlyList<ModFileEntry> Files { get; set; } = [];
        public bool FailReads { get; set; }
        public int Reads { get; private set; }
        public int PasswordChecks { get; private set; }

        public Task<IReadOnlyList<ModFileEntry>> ScanAsync(string operation, CancellationToken cancellationToken) =>
            Task.FromResult(Files);

        public async Task ReadFileAsync(string operation, string fileName, Stream destination, CancellationToken cancellationToken)
        {
            if (FailReads)
            {
                throw new ModTargetException("read failed");
            }

            Reads++;
            await destination.WriteAsync(Encoding.UTF8.GetBytes(fileName), cancellationToken);
        }

        public Task<bool> CheckServerPasswordAsync(string operation, string password, CancellationToken cancellationToken)
        {
            PasswordChecks++;
            return Task.FromResult(password == "right");
        }

        public Task<GameInfo> GetGameInfoAsync(string operation, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task StageAsync(string operation, string batchId, string fileName, Stream content, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ModJournal> ApplyAsync(string operation, string batchId, IReadOnlyList<ModJournalItem> items, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DiscardAsync(string operation, string batchId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ModJournal?> GetBackupAsync(string operation, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ModJournal> RollbackAsync(string operation, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
