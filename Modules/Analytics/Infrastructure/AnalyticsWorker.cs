using AlegacyWebPanel.Modules.Analytics.Configuration;
using AlegacyWebPanel.Modules.Analytics.Services;
using Microsoft.Extensions.Options;

namespace AlegacyWebPanel.Modules.Analytics.Infrastructure;

public sealed class AnalyticsWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<AnalyticsOptions> options,
    TimeProvider time,
    ILogger<AnalyticsWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PruneInterval = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            return;
        }

        if (!await RunAsync(recorder => recorder.InitializeAsync(stoppingToken), "initialize the analytics store"))
        {
            return;
        }

        var importInterval = TimeSpan.FromMinutes(settings.EventImportIntervalMinutes);
        var lastImport = DateTimeOffset.MinValue;
        var lastPrune = DateTimeOffset.MinValue;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(settings.SampleIntervalSeconds), time);

        do
        {
            var now = time.GetUtcNow();
            if (now - lastImport >= importInterval)
            {
                await RunAsync(recorder => recorder.ImportPlayerEventsAsync(stoppingToken), "import player events");
                lastImport = now;
            }

            await RunAsync(recorder => recorder.RecordConnectionsAsync(stoppingToken), "record connection samples");

            if (now - lastPrune >= PruneInterval)
            {
                await RunAsync(recorder => recorder.PruneAsync(stoppingToken), "prune analytics data");
                lastPrune = now;
            }
        }
        while (await WaitAsync(timer, stoppingToken));

        async Task<bool> RunAsync(Func<IAnalyticsRecorder, Task> step, string description)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await step(scope.ServiceProvider.GetRequiredService<IAnalyticsRecorder>());
                return true;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return false;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Failed to {Step}", description);
                return false;
            }
        }
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
