using AlegacyWebPanel.Modules.Authentication.Contracts;
using AlegacyWebPanel.Modules.Authentication.Persistence;
using AlegacyWebPanel.Modules.Users.Persistence;

namespace AlegacyWebPanel.Modules.Authentication.Services;

public sealed class LoginLogService(ILoginEventRepository repository) : ILoginLogService
{
    public const int MaxPageSize = 50;

    public Task RecordLoginAsync(string? username, string ipAddress, bool succeeded, CancellationToken cancellationToken)
    {
        var entry = new LoginEvent
        {
            Username = string.IsNullOrWhiteSpace(username) ? "unknown" : username,
            IpAddress = string.IsNullOrWhiteSpace(ipAddress) ? "unknown" : ipAddress,
            Succeeded = succeeded,
            OccurredAt = DateTime.UtcNow
        };

        return repository.RecordAsync(entry, cancellationToken);
    }

    public async Task<LoginLogPageDto> GetRecentLoginsAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        if (page < 1)
        {
            page = 1;
        }

        if (pageSize < 1)
        {
            pageSize = 10;
        }

        pageSize = Math.Min(pageSize, MaxPageSize);

        var total = await repository.CountAsync(cancellationToken);
        var offset = (page - 1) * pageSize;
        var items = await repository.GetPageAsync(offset, pageSize, cancellationToken);

        return new LoginLogPageDto(items, total, offset, pageSize);
    }
}
