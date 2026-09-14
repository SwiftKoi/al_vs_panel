using AlegacyWebPanel.Modules.Authentication.Contracts;
using AlegacyWebPanel.Modules.Authentication.Persistence;
using AlegacyWebPanel.Modules.Authentication.Services;
using AlegacyWebPanel.Modules.Users.Persistence;

namespace AlegacyWebPanel.Authentication.UnitTests;

public sealed class LoginLogServiceTests
{
    [Fact]
    public async Task RecordLogin_maps_entry_without_http_types()
    {
        var repository = new FakeLoginEventRepository();
        var service = new LoginLogService(repository);

        await service.RecordLoginAsync("admin", "192.168.1.100", succeeded: true, CancellationToken.None);

        var entry = Assert.Single(repository.Recorded);
        Assert.Equal("admin", entry.Username);
        Assert.Equal("192.168.1.100", entry.IpAddress);
        Assert.True(entry.Succeeded);
        Assert.True(entry.OccurredAt <= DateTime.UtcNow);
    }

    [Fact]
    public async Task RecordLogin_falls_back_to_unknown_when_values_missing()
    {
        var repository = new FakeLoginEventRepository();
        var service = new LoginLogService(repository);

        await service.RecordLoginAsync(null, string.Empty, succeeded: false, CancellationToken.None);

        var entry = Assert.Single(repository.Recorded);
        Assert.Equal("unknown", entry.Username);
        Assert.Equal("unknown", entry.IpAddress);
        Assert.False(entry.Succeeded);
    }

    [Fact]
    public async Task GetRecentLogins_returns_newest_first_page_with_metadata()
    {
        var items = new[]
        {
            new LoginEventDto(3, "a", DateTime.UtcNow, "1.1.1.1", true),
            new LoginEventDto(2, "b", DateTime.UtcNow.AddMinutes(-1), "2.2.2.2", false),
            new LoginEventDto(1, "c", DateTime.UtcNow.AddMinutes(-2), "3.3.3.3", true)
        };
        var repository = new FakeLoginEventRepository { Total = 23, PageItems = items };
        var service = new LoginLogService(repository);

        var page = await service.GetRecentLoginsAsync(3, 10, CancellationToken.None);

        Assert.Equal(3, page.Items.Count);
        Assert.Equal(23, page.Total);
        Assert.Equal(20, page.Offset);
        Assert.Equal(10, page.Limit);
        Assert.Equal(20, repository.RequestedOffset);
        Assert.Equal(10, repository.RequestedLimit);
    }

    [Fact]
    public async Task GetRecentLogins_clamps_page_and_page_size()
    {
        var repository = new FakeLoginEventRepository { Total = 5, PageItems = [] };
        var service = new LoginLogService(repository);

        var page = await service.GetRecentLoginsAsync(0, 0, CancellationToken.None);

        Assert.Equal(0, page.Offset);
        Assert.Equal(10, page.Limit);
        Assert.Equal(0, repository.RequestedOffset);
        Assert.Equal(10, repository.RequestedLimit);
    }

    [Fact]
    public async Task GetRecentLogins_caps_page_size_at_maximum()
    {
        var repository = new FakeLoginEventRepository { Total = 500, PageItems = [] };
        var service = new LoginLogService(repository);

        var page = await service.GetRecentLoginsAsync(1, 5000, CancellationToken.None);

        Assert.Equal(LoginLogService.MaxPageSize, page.Limit);
        Assert.Equal(LoginLogService.MaxPageSize, repository.RequestedLimit);
    }

    private sealed class FakeLoginEventRepository : ILoginEventRepository
    {
        public List<LoginEvent> Recorded { get; } = [];
        public int Total { get; init; }
        public IReadOnlyList<LoginEventDto> PageItems { get; init; } = [];
        public int RequestedOffset { get; private set; }
        public int RequestedLimit { get; private set; }

        public Task RecordAsync(LoginEvent entry, CancellationToken cancellationToken)
        {
            Recorded.Add(entry);
            return Task.CompletedTask;
        }

        public Task<int> CountAsync(CancellationToken cancellationToken) => Task.FromResult(Total);

        public Task<IReadOnlyList<LoginEventDto>> GetPageAsync(int offset, int limit, CancellationToken cancellationToken)
        {
            RequestedOffset = offset;
            RequestedLimit = limit;
            return Task.FromResult(PageItems);
        }
    }
}
