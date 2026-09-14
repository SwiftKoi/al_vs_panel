using System.Threading.Channels;
using AlegacyWebPanel.Core.Security;
using AlegacyWebPanel.Modules.Logging.Configuration;
using AlegacyWebPanel.Modules.Logging.Contracts;
using AlegacyWebPanel.Modules.Logging.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AlegacyWebPanel.Logging.UnitTests;

public sealed class SqliteLogWriterTests
{
    private static (Channel<LogEntry> Channel, SqliteLogWriter Writer, LogDropCounter Drops) Create(
        LogLevel minimumLevel = LogLevel.Information,
        int capacity = 16,
        int stateMaxBytes = 4096)
    {
        var channel = Channel.CreateBounded<LogEntry>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait
        });
        var drops = new LogDropCounter();
        var writer = new SqliteLogWriter("Test.Category", minimumLevel, stateMaxBytes, channel, drops);
        return (channel, writer, drops);
    }

    [Fact]
    public void Log_writes_entry_with_metadata_and_formatted_message()
    {
        var (channel, writer, _) = Create();

        writer.LogInformation("hello {Name}", "world");

        Assert.True(channel.Reader.TryRead(out var entry));
        Assert.Equal(0, entry.Id);
        Assert.Equal(LogLevel.Information.ToString(), entry.Level);
        Assert.Equal("Test.Category", entry.Category);
        Assert.Equal("hello world", entry.Message);
    }

    [Fact]
    public void Log_filters_entries_below_minimum_level()
    {
        var (channel, writer, _) = Create(minimumLevel: LogLevel.Warning);

        writer.LogInformation("noise");
        writer.LogWarning("caution");

        Assert.True(channel.Reader.TryRead(out var entry));
        Assert.Equal(LogLevel.Warning.ToString(), entry.Level);
        Assert.False(channel.Reader.TryRead(out _));
    }

    [Fact]
    public void Log_ignores_none_level()
    {
        var (channel, writer, _) = Create();

        writer.Log(LogLevel.None, default, "ignored", null, static (_, _) => "ignored");

        Assert.False(channel.Reader.TryRead(out _));
    }

    [Fact]
    public void Log_serializes_structured_state_to_json()
    {
        var (channel, writer, _) = Create();

        writer.LogInformation("process {Id} finished", 42);

        Assert.True(channel.Reader.TryRead(out var entry));
        Assert.Equal("{\"Id\":42}", entry.StateJson);
    }

    [Fact]
    public void Log_redacts_secret_values_in_state()
    {
        var (channel, writer, _) = Create();

        writer.LogInformation("token {Token} used", new SecretValue("super-secret-value"));

        Assert.True(channel.Reader.TryRead(out var entry));
        Assert.DoesNotContain("super-secret-value", entry.StateJson);
        Assert.Contains("[REDACTED]", entry.StateJson);
    }

    [Fact]
    public void Log_captures_exception_details()
    {
        var (channel, writer, _) = Create();
        var exception = new InvalidOperationException("boom");

        writer.LogError(exception, "operation failed");

        Assert.True(channel.Reader.TryRead(out var entry));
        Assert.Equal(LogLevel.Error.ToString(), entry.Level);
        Assert.Equal("boom", entry.ExceptionMessage);
        Assert.Equal(typeof(InvalidOperationException).FullName, entry.ExceptionType);
        Assert.Contains("boom", entry.StackTrace);
    }

    [Fact]
    public void Log_truncates_long_message_and_stack_trace()
    {
        var (channel, writer, _) = Create();
        var longMessage = new string('x', 20_000);
        var exception = new InvalidOperationException(new string('y', 30_000));

        writer.LogError(exception, longMessage);

        Assert.True(channel.Reader.TryRead(out var entry));
        Assert.True(entry.Message.Length <= 8000);
        Assert.True(entry.StackTrace!.Length <= 16000);
    }

    [Fact]
    public void Log_bounds_oversized_structured_state()
    {
        var (channel, writer, _) = Create(stateMaxBytes: 200);

        writer.LogInformation("payload {Data}", new string('a', 1000));

        Assert.True(channel.Reader.TryRead(out var entry));
        Assert.True(entry.StateJson!.Length <= 200);
    }

    [Fact]
    public void Log_drops_entries_when_channel_is_full_without_blocking()
    {
        var (channel, writer, drops) = Create(capacity: 1);

        writer.LogInformation("first");
        writer.LogInformation("second");

        Assert.Equal(1, drops.Dropped);
        Assert.True(channel.Reader.TryRead(out _));
        Assert.False(channel.Reader.TryRead(out _));
    }

    [Fact]
    public void IsEnabled_reflects_minimum_level()
    {
        var (_, writer, _) = Create(minimumLevel: LogLevel.Error);

        Assert.False(writer.IsEnabled(LogLevel.Information));
        Assert.False(writer.IsEnabled(LogLevel.Warning));
        Assert.True(writer.IsEnabled(LogLevel.Error));
        Assert.True(writer.IsEnabled(LogLevel.Critical));
    }

    [Fact]
    public void Provider_suppresses_ef_core_noise_below_warning()
    {
        var provider = new SqliteLogProvider(
            Options.Create(new LogStoreOptions { MinimumLevel = "Debug" }),
            Channel.CreateBounded<LogEntry>(new BoundedChannelOptions(16)
            {
                FullMode = BoundedChannelFullMode.Wait
            }),
            new LogDropCounter());

        var efLogger = provider.CreateLogger("Microsoft.EntityFrameworkCore.Database.Command");
        var appLogger = provider.CreateLogger("AlegacyWebPanel.Something");

        Assert.True(appLogger.IsEnabled(LogLevel.Debug));
        Assert.False(efLogger.IsEnabled(LogLevel.Debug));
        Assert.False(efLogger.IsEnabled(LogLevel.Information));
        Assert.True(efLogger.IsEnabled(LogLevel.Warning));
        Assert.True(efLogger.IsEnabled(LogLevel.Error));
    }
}
