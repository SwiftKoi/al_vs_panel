using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using AlegacyWebPanel.Modules.FileManager.Configuration;
using AlegacyWebPanel.Modules.FileManager.Contracts;
using AlegacyWebPanel.Modules.FileManager.Exceptions;
using Microsoft.Extensions.Options;

namespace AlegacyWebPanel.Modules.FileManager.Services;

public interface IBackgroundOperationTracker
{
    /// <summary>Starts a tracked operation; throws <see cref="TooManyOperationsException"/> when the concurrency limit is reached.</summary>
    string StartTracking(
        string description,
        OperationTarget? target,
        Func<CancellationToken, IProgress<OperationProgress>, Task> taskFunc);

    TrackedOperationDto? GetStatus(string taskId);

    /// <summary>Recent operations, newest first (running ones and those finished within the retention window).</summary>
    IReadOnlyList<TrackedOperationDto> GetRecent();

    /// <summary>Requests cancellation; false when the task is unknown or already finished.</summary>
    bool Cancel(string taskId);
}

public sealed class BackgroundOperationTracker : IBackgroundOperationTracker
{
    // Finished operations stay queryable this long so pollers and the history list can show the result.
    private static readonly TimeSpan CompletedRetention = TimeSpan.FromHours(1);

    private readonly ConcurrentDictionary<string, TaskState> _tasks = new();
    private readonly int _maximumConcurrent;
    private readonly TimeSpan _timeout;
    private readonly TimeProvider _timeProvider;
    private int _running;

    public BackgroundOperationTracker()
        : this(Options.Create(new FileManagerOptions()), TimeProvider.System)
    {
    }

    public BackgroundOperationTracker(IOptions<FileManagerOptions> options)
        : this(options, TimeProvider.System)
    {
    }

    public BackgroundOperationTracker(IOptions<FileManagerOptions> options, TimeProvider timeProvider)
    {
        _maximumConcurrent = options.Value.MaximumConcurrentOperations;
        _timeout = TimeSpan.FromMinutes(options.Value.OperationTimeoutMinutes);
        _timeProvider = timeProvider;
    }

    public string StartTracking(
        string description,
        OperationTarget? target,
        Func<CancellationToken, IProgress<OperationProgress>, Task> taskFunc)
    {
        PruneCompleted();

        if (Interlocked.Increment(ref _running) > _maximumConcurrent)
        {
            Interlocked.Decrement(ref _running);
            throw new TooManyOperationsException(_maximumConcurrent);
        }

        var taskId = Guid.NewGuid().ToString("N");
        // A stuck helper (huge archive, hung filesystem) is killed after the timeout
        // instead of occupying a concurrency slot forever; users can also cancel.
        var cancellation = new CancellationTokenSource(_timeout, _timeProvider);
        var state = new TaskState(taskId, description, target, _timeProvider.GetUtcNow(), cancellation);
        _tasks[taskId] = state;

        _ = Task.Run(async () =>
        {
            try
            {
                await taskFunc(cancellation.Token, new Progress(state));
                state.Status = "Completed";
            }
            catch (Exception) when (state.CancelRequested)
            {
                state.Status = "Cancelled";
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                state.Status = "Failed";
                state.ErrorMessage = $"The operation did not finish within {_timeout.TotalMinutes:0} minutes and was stopped.";
            }
            catch (Exception ex)
            {
                state.Status = "Failed";
                state.ErrorMessage = ex.Message;
            }
            finally
            {
                state.Completed = _timeProvider.GetUtcNow();
                cancellation.Dispose();
                Interlocked.Decrement(ref _running);
            }
        });

        return taskId;
    }

    public TrackedOperationDto? GetStatus(string taskId)
    {
        PruneCompleted();
        return _tasks.TryGetValue(taskId, out var state) ? ToDto(state) : null;
    }

    public IReadOnlyList<TrackedOperationDto> GetRecent()
    {
        PruneCompleted();
        return _tasks.Values.OrderByDescending(state => state.Created).Select(ToDto).ToList();
    }

    public bool Cancel(string taskId)
    {
        if (!_tasks.TryGetValue(taskId, out var state) || state.Completed is not null)
        {
            return false;
        }

        state.CancelRequested = true;
        try
        {
            state.Cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Finished in the meantime.
            return false;
        }

        return true;
    }

    private static TrackedOperationDto ToDto(TaskState state) => new(
        state.TaskId,
        state.Description,
        state.Status,
        state.ErrorMessage,
        state.Created,
        state.Completed,
        state.Progress,
        state.Target);

    private void PruneCompleted()
    {
        var cutoff = _timeProvider.GetUtcNow() - CompletedRetention;
        foreach (var (taskId, state) in _tasks)
        {
            if (state.Completed is { } completed && completed < cutoff)
            {
                _tasks.TryRemove(taskId, out _);
            }
        }
    }

    /// <summary>Stores the latest report directly (no SynchronizationContext capture, unlike Progress&lt;T&gt;).</summary>
    private sealed class Progress(TaskState state) : IProgress<OperationProgress>
    {
        public void Report(OperationProgress value) => state.Progress = value;
    }

    private sealed class TaskState(
        string taskId,
        string description,
        OperationTarget? target,
        DateTimeOffset created,
        CancellationTokenSource cancellation)
    {
        private volatile string _status = "Running";
        private volatile string? _errorMessage;
        private volatile OperationProgress? _progress;
        private volatile bool _cancelRequested;

        public string TaskId { get; } = taskId;
        public string Description { get; } = description;
        public OperationTarget? Target { get; } = target;
        public DateTimeOffset Created { get; } = created;
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public string Status { get => _status; set => _status = value; }
        public string? ErrorMessage { get => _errorMessage; set => _errorMessage = value; }
        public OperationProgress? Progress { get => _progress; set => _progress = value; }
        public bool CancelRequested { get => _cancelRequested; set => _cancelRequested = value; }
        public DateTimeOffset? Completed { get; set; }
    }
}
