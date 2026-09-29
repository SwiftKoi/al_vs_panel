namespace AlegacyWebPanel.Modules.Users.Contracts;

public sealed record CreateUserRequest(string Username, string Password, string Role);

public sealed record ChangeRoleRequest(string Role);

public sealed record UserResponse(string Id, string Username, bool TwoFactorEnabled, string Role);
