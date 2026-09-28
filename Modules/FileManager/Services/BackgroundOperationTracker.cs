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
    string StartTracking(string description, Func<CancellationToken, Task> taskFunc);
    TrackedOperationDto? GetStatus(string taskId);
}

public sealed class BackgroundOperationTracker : IBackgroundOperationTracker
{
    // Finished operations stay queryable this long so pollers can pick up the result.
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

    public string StartTracking(string description, Func<CancellationToken, Task> taskFunc)
    {
        PruneCompleted();

        if (Interlocked.Increment(ref _running) > _maximumConcurrent)
        {
            Interlocked.Decrement(ref _running);
            throw new TooManyOperationsException(_maximumConcurrent);
        }

        var taskId = Guid.NewGuid().ToString("N");
        var state = new TaskState(taskId, description, _timeProvider.GetUtcNow());
        _tasks[taskId] = state;

        _ = Task.Run(async () =>
        {
            // A stuck helper (huge archive, hung filesystem) is killed after the timeout
            // instead of occupying a concurrency slot forever.
            using var timeout = new CancellationTokenSource(_timeout, _timeProvider);
            try
            {
                await taskFunc(timeout.Token);
                state.Status = "Completed";
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
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
                Interlocked.Decrement(ref _running);
            }
        });

        return taskId;
    }

    public TrackedOperationDto? GetStatus(string taskId)
    {
        PruneCompleted();

        if (!_tasks.TryGetValue(taskId, out var state))
        {
            return null;
        }

        return new TrackedOperationDto(
            state.TaskId,
            state.Description,
            state.Status,
            state.ErrorMessage,
            state.Created,
            state.Completed);
    }

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

    private sealed class TaskState(string taskId, string description, DateTimeOffset created)
    {
        private volatile string _status = "Running";
        private volatile string? _errorMessage;

        public string TaskId { get; } = taskId;
        public string Description { get; } = description;
        public DateTimeOffset Created { get; } = created;
        public string Status { get => _status; set => _status = value; }
        public string? ErrorMessage { get => _errorMessage; set => _errorMessage = value; }
        public DateTimeOffset? Completed { get; set; }
    }
}
