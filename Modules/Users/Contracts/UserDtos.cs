namespace AlegacyWebPanel.Modules.Users.Contracts;

public sealed record CreateUserRequest(string Username, string Password);

public sealed record UserResponse(string Id, string Username, bool TwoFactorEnabled);
