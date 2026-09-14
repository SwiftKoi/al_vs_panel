namespace AlegacyWebPanel.Modules.Logging.Infrastructure;

public sealed class LogDropCounter
{
    private long _dropped;

    public long Dropped => Interlocked.Read(ref _dropped);

    public void Increment() => Interlocked.Increment(ref _dropped);
}
