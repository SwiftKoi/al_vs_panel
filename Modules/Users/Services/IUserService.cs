using AlegacyWebPanel.Modules.Users.Contracts;

namespace AlegacyWebPanel.Modules.Users.Services;

public interface IUserService
{
    Task<IEnumerable<UserResponse>> ListUsersAsync(CancellationToken cancellationToken);
    Task<UserResponse> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken);
    Task DeleteUserAsync(string userId, string currentUserId, CancellationToken cancellationToken);
}
