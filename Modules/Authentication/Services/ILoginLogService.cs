using AlegacyWebPanel.Modules.Authentication.Contracts;

namespace AlegacyWebPanel.Modules.Authentication.Services;

public interface ILoginLogService
{
    Task RecordLoginAsync(string? username, string ipAddress, bool succeeded, CancellationToken cancellationToken);
    Task<LoginLogPageDto> GetRecentLoginsAsync(int page, int pageSize, CancellationToken cancellationToken);
}
