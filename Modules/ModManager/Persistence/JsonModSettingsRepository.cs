using System.Text.Json;

namespace AlegacyWebPanel.Modules.ModManager.Persistence;

/// <summary>Small JSON document next to the panel's other state: { serverId: { pinned: [modid, ...] } }.</summary>
public sealed class JsonModSettingsRepository(string path) : IModSettingsRepository
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<IReadOnlySet<string>> GetPinnedAsync(string serverId, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var document = await LoadAsync(cancellationToken);
            return document.TryGetValue(serverId, out var settings)
                ? settings.Pinned.ToHashSet(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SetPinnedAsync(string serverId, string modId, bool pinned, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var document = await LoadAsync(cancellationToken);
            if (!document.TryGetValue(serverId, out var settings))
            {
                document[serverId] = settings = new ServerSettings();
            }

            settings.Pinned.RemoveAll(item => item.Equals(modId, StringComparison.OrdinalIgnoreCase));
            if (pinned)
            {
                settings.Pinned.Add(modId);
                settings.Pinned.Sort(StringComparer.OrdinalIgnoreCase);
            }

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temp = path + ".tmp";
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(document), cancellationToken);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<Dictionary<string, ServerSettings>> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        var text = await File.ReadAllTextAsync(path, cancellationToken);
        return JsonSerializer.Deserialize<Dictionary<string, ServerSettings>>(text) ?? [];
    }

    private sealed class ServerSettings
    {
        public List<string> Pinned { get; set; } = [];
    }
}
