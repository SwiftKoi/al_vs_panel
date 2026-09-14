using System.Collections.Concurrent;

namespace AlegacyWebPanel.Modules.ServerManagement.Services;

public interface IServerLifecycleOperationGuard
{
    bool TryEnter(string serverId, out IDisposable lease);
}

public sealed class ServerLifecycleOperationGuard : IServerLifecycleOperationGuard
{
    private readonly ConcurrentDictionary<string, byte> _active = new(StringComparer.Ordinal);

    public bool TryEnter(string serverId, out IDisposable lease)
    {
        if (!_active.TryAdd(serverId, 0))
        {
            lease = NullLease.Instance;
            return false;
        }

        lease = new Lease(_active, serverId);
        return true;
    }

    private sealed class Lease(ConcurrentDictionary<string, byte> active, string serverId) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                active.TryRemove(serverId, out _);
            }
        }
    }

    private sealed class NullLease : IDisposable
    {
        public static readonly NullLease Instance = new();
        public void Dispose() { }
    }
}
