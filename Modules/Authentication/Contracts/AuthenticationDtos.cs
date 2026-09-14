namespace AlegacyWebPanel.Modules.Authentication.Contracts;

public sealed record LoginRequest(string Username, string Password);

public sealed record LoginCommand(string Username, string Password);

public sealed record AuthenticatedUser(string Id, string Username);

public sealed record LoginResponse(
    bool Authenticated,
    bool RequiresTwoFactor = false,
    AuthenticatedUser? User = null);

public sealed record AuthenticationSessionResponse(bool Authenticated, AuthenticatedUser? User);

public sealed record CsrfTokenResponse(string Token);

public sealed record TwoFactorLoginRequest(string Code);

public sealed record EnableTwoFactorRequest(string Code);

public sealed record TwoFactorSetupResponse(string SharedSecret, string ProvisioningUri);

public sealed record LoginEventDto(int Id, string Username, DateTime TimestampUtc, string IpAddress, bool Succeeded);

public sealed record LoginLogPageDto(IReadOnlyList<LoginEventDto> Items, int Total, int Offset, int Limit);
