using System.IO.Compression;
using AlegacyWebPanel.Modules.ModManager.Contracts;
using AlegacyWebPanel.Modules.ModManager.Exceptions;
using AlegacyWebPanel.Modules.ModManager.Persistence;

namespace AlegacyWebPanel.Modules.ModManager.Services;

public sealed record ModUpdatePlanItem(
    string ModId,
    string Name,
    string InstalledFileName,
    string? FromVersion,
    ModDbRelease Release);

/// <summary>Keeps the current (or last) update job per server and runs it in the background.</summary>
public interface IModUpdateJobTracker
{
    ModUpdateJobDto? Get(string serverId);

    /// <summary>Returns null when a job is already running for the server.</summary>
    ModUpdateJobDto? Start(string serverId, string operation, IReadOnlyList<ModUpdatePlanItem> plan);
}

public sealed class ModUpdateJobTracker(
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime lifetime,
    TimeProvider timeProvider,
    ILogger<ModUpdateJobTracker> logger) : IModUpdateJobTracker
{
    private readonly Dictionary<string, ModUpdateJob> _jobs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _lock = new();

    public ModUpdateJobDto? Get(string serverId)
    {
        lock (_lock)
        {
            return _jobs.TryGetValue(serverId, out var job) ? job.ToDto() : null;
        }
    }

    public ModUpdateJobDto? Start(string serverId, string operation, IReadOnlyList<ModUpdatePlanItem> plan)
    {
        ModUpdateJob job;
        lock (_lock)
        {
            if (_jobs.TryGetValue(serverId, out var existing) && existing.State == ModUpdateJobState.Running)
            {
                return null;
            }

            job = new ModUpdateJob(Guid.NewGuid().ToString("N")[..12], serverId, operation, plan, timeProvider.GetUtcNow(), _lock);
            _jobs[serverId] = job;
        }

        _ = Task.Run(() => RunAsync(job), CancellationToken.None);
        return job.ToDto();
    }

    private async Task RunAsync(ModUpdateJob job)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var runner = scope.ServiceProvider.GetRequiredService<ModUpdateRunner>();
            await runner.RunAsync(job, lifetime.ApplicationStopping);
            job.Finish(null, timeProvider.GetUtcNow());
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Mod update {JobId} on {ServerId} failed", job.Id, job.ServerId);
            job.Finish(exception is OperationCanceledException ? "The update was cancelled." : exception.Message, timeProvider.GetUtcNow());
        }
    }
}

/// <summary>Mutable job state; every access goes through the tracker's lock.</summary>
public sealed class ModUpdateJob
{
    private readonly Lock _lock;
    private readonly ModUpdateItemState[] _states;
    private readonly string?[] _errors;
    private ModUpdateJobState _state = ModUpdateJobState.Running;
    private DateTimeOffset? _finishedUtc;
    private string? _error;

    internal ModUpdateJob(string id, string serverId, string operation, IReadOnlyList<ModUpdatePlanItem> plan, DateTimeOffset startedUtc, Lock sync)
    {
        Id = id;
        ServerId = serverId;
        Operation = operation;
        Plan = plan;
        StartedUtc = startedUtc;
        _lock = sync;
        _states = new ModUpdateItemState[plan.Count];
        _errors = new string?[plan.Count];
    }

    public string Id { get; }
    public string ServerId { get; }
    public string Operation { get; }
    public IReadOnlyList<ModUpdatePlanItem> Plan { get; }
    public DateTimeOffset StartedUtc { get; }

    public ModUpdateJobState State
    {
        get { lock (_lock) return _state; }
    }

    public ModUpdateItemState GetItemState(int index)
    {
        lock (_lock) return _states[index];
    }

    public void SetItem(int index, ModUpdateItemState state, string? error = null)
    {
        lock (_lock)
        {
            _states[index] = state;
            _errors[index] = error;
        }
    }

    internal void Finish(string? error, DateTimeOffset finishedUtc)
    {
        lock (_lock)
        {
            _error = error;
            _finishedUtc = finishedUtc;
            var anyInstalled = _states.Any(state => state == ModUpdateItemState.Installed);
            _state = error is null && anyInstalled ? ModUpdateJobState.Succeeded : ModUpdateJobState.Failed;
            _error ??= anyInstalled ? null : "No mod could be updated.";
            for (var index = 0; index < _states.Length; index++)
            {
                if (_states[index] is not (ModUpdateItemState.Installed or ModUpdateItemState.Failed))
                {
                    _states[index] = ModUpdateItemState.Failed;
                    _errors[index] ??= error ?? "Not applied.";
                }
            }
        }
    }

    internal ModUpdateJobDto ToDto()
    {
        lock (_lock)
        {
            return new ModUpdateJobDto(Id, ServerId, _state, StartedUtc, _finishedUtc, _error,
                Plan.Select((item, index) => new ModUpdateJobItemDto(
                    item.ModId, item.Name, item.FromVersion, item.Release.Version, _states[index], _errors[index])).ToArray());
        }
    }
}

/// <summary>
/// download → verify (archive shape, modid and version match the plan) → stage on the server →
/// one atomic swap of every verified file. Mods that fail before the swap are skipped; nothing in
/// Mods changes until the swap, and the swap itself is all-or-nothing on the helper side.
/// </summary>
public sealed class ModUpdateRunner(
    IModDbRepository modDb,
    IModTargetRepository target,
    ILogger<ModUpdateRunner> logger)
{
    private const int MaximumArchiveEntries = 50000;

    public async Task RunAsync(ModUpdateJob job, CancellationToken cancellationToken)
    {
        var staged = new List<ModJournalItem>();
        var stagedIndexes = new List<int>();
        var tempDirectory = Directory.CreateTempSubdirectory("mod-update-");
        try
        {
            for (var index = 0; index < job.Plan.Count; index++)
            {
                var item = job.Plan[index];
                try
                {
                    job.SetItem(index, ModUpdateItemState.Downloading);
                    var fileName = SafeFileName(item.Release.FileName!);
                    var path = Path.Combine(tempDirectory.FullName, fileName);
                    await using (var file = File.Create(path))
                    {
                        await modDb.DownloadAsync(item.Release.MainFileUrl!, file, cancellationToken);
                    }

                    job.SetItem(index, ModUpdateItemState.Verifying);
                    Verify(path, item);

                    await using (var file = File.OpenRead(path))
                    {
                        await target.StageAsync(job.Operation, job.Id, fileName, file, cancellationToken);
                    }

                    staged.Add(new ModJournalItem(item.InstalledFileName, fileName));
                    stagedIndexes.Add(index);
                    job.SetItem(index, ModUpdateItemState.Staged);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogWarning(exception, "Update of {ModId} to {Version} failed", item.ModId, item.Release.Version);
                    job.SetItem(index, ModUpdateItemState.Failed, exception.Message);
                }
            }

            if (staged.Count == 0)
            {
                return;
            }

            await target.ApplyAsync(job.Operation, job.Id, staged, cancellationToken);
            foreach (var index in stagedIndexes)
            {
                job.SetItem(index, ModUpdateItemState.Installed);
            }

            logger.LogInformation("Mod update {JobId} on {ServerId} installed {Count} mod(s)", job.Id, job.ServerId, staged.Count);
        }
        catch
        {
            await DiscardQuietlyAsync(job);
            throw;
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    private static void Verify(string path, ModUpdatePlanItem item)
    {
        try
        {
            using var archive = ZipFile.OpenRead(path);
            if (archive.Entries.Count > MaximumArchiveEntries)
            {
                throw new ModValidationException("The archive contains too many entries.");
            }

            if (archive.Entries.Any(entry => entry.FullName.StartsWith('/') || entry.FullName.StartsWith('\\') ||
                                             entry.FullName.Split('/', '\\').Contains("..")))
            {
                throw new ModValidationException("The archive contains unsafe paths.");
            }

            var modInfoEntry = archive.Entries.FirstOrDefault(entry => entry.FullName.Equals("modinfo.json", StringComparison.OrdinalIgnoreCase))
                               ?? throw new ModValidationException("The download has no modinfo.json.");
            using var stream = modInfoEntry.Open();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            var info = ModInfoParser.TryParse(buffer.ToArray())
                       ?? throw new ModValidationException("The download's modinfo.json could not be read.");
            if (!info.ModId.Equals(item.ModId, StringComparison.OrdinalIgnoreCase))
            {
                throw new ModValidationException($"The download is mod '{info.ModId}', not '{item.ModId}'.");
            }

            if (ModVersion.Compare(info.Version, item.Release.Version) != 0)
            {
                throw new ModValidationException($"The download is version {info.Version}, not {item.Release.Version}.");
            }
        }
        catch (InvalidDataException)
        {
            throw new ModValidationException("The download is not a valid zip archive.");
        }
    }

    private static string SafeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName.Replace('\\', '/')).Trim();
        if (name.Length == 0 || name.StartsWith('.') || !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ModValidationException($"ModDB file name '{fileName}' is not a safe mod archive name.");
        }

        return name;
    }

    private async Task DiscardQuietlyAsync(ModUpdateJob job)
    {
        try
        {
            await target.DiscardAsync(job.Operation, job.Id, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not discard staged files of {JobId}", job.Id);
        }
    }
}
