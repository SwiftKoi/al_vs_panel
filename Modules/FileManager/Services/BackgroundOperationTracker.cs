using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using AlegacyWebPanel.Modules.FileManager.Contracts;

namespace AlegacyWebPanel.Modules.FileManager.Services;

public interface IBackgroundOperationTracker
{
    string StartTracking(string description, Func<CancellationToken, Task> taskFunc);
    TrackedOperationDto? GetStatus(string taskId);
}

public sealed class BackgroundOperationTracker : IBackgroundOperationTracker
{
    private readonly ConcurrentDictionary<string, TaskState> _tasks = new();

    public string StartTracking(string description, Func<CancellationToken, Task> taskFunc)
    {
        var taskId = Guid.NewGuid().ToString("N");
        var state = new TaskState(taskId, description);
        state.Status = "Running";
        _tasks[taskId] = state;

        _ = Task.Run(async () =>
        {
            try
            {
                await taskFunc(CancellationToken.None);
                state.Status = "Completed";
            }
            catch (Exception ex)
            {
                state.Status = "Failed";
                state.ErrorMessage = ex.Message;
            }
            finally
            {
                state.Completed = DateTimeOffset.UtcNow;
            }
        });

        return taskId;
    }

    public TrackedOperationDto? GetStatus(string taskId)
    {
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

    private sealed class TaskState(string taskId, string description)
    {
        public string TaskId { get; } = taskId;
        public string Description { get; } = description;
        public string Status { get; set; } = "Pending";
        public string? ErrorMessage { get; set; }
        public DateTimeOffset Created { get; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? Completed { get; set; }
    }
}
