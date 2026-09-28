using AlegacyWebPanel.Modules.Analytics.Contracts;

namespace AlegacyWebPanel.Modules.Analytics.Persistence;

public interface IAnalyticsRepository
{
    // Creates missing tables and indexes. Existing tables are left untouched, so
    // schema changes must be additive (new tables or indexes).
    Task EnsureCreatedAsync(CancellationToken cancellationToken);

    // Inserts joins that are not stored yet and returns how many were added.
    Task<int> AddJoinsAsync(IReadOnlyList<PlayerJoin> joins, CancellationToken cancellationToken);

    // Inserts events that are not stored yet and returns how many were added.
    Task<int> AddEventsAsync(PlayerEventBatch batch, CancellationToken cancellationToken);

    Task AddMetricSampleAsync(ServerMetricSample sample, CancellationToken cancellationToken);

    Task<IReadOnlyList<ServerMetricSample>> ListMetricSamplesAsync(
        string serverId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken);

    Task AddSamplesAsync(IReadOnlyList<ConnectionSample> samples, CancellationToken cancellationToken);

    Task<IReadOnlyList<PlayerJoin>> ListJoinsAsync(
        string serverId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PlayerSessionEnd>> ListSessionEndsAsync(
        string serverId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ConnectionFailure>> ListFailuresAsync(
        string serverId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ServerPause>> ListPausesAsync(
        string serverId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ServerOverload>> ListOverloadsAsync(
        string serverId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ConnectionSample>> ListSamplesAsync(
        string serverId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, DateTime>> GetFirstJoinsAsync(
        string serverId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SampleRoundCount>> ListSampleRoundsAsync(
        string serverId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken);

    Task PruneAsync(DateTime samplesBeforeUtc, DateTime eventsBeforeUtc, CancellationToken cancellationToken);
}
