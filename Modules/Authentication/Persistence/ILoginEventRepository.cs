using AlegacyWebPanel.Modules.Authentication.Contracts;
using AlegacyWebPanel.Modules.Users.Persistence;

namespace AlegacyWebPanel.Modules.Authentication.Persistence;

public interface ILoginEventRepository
{
    Task RecordAsync(LoginEvent entry, CancellationToken cancellationToken);
    Task<int> CountAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<LoginEventDto>> GetPageAsync(int offset, int limit, CancellationToken cancellationToken);
}
