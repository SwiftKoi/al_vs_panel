namespace AlegacyWebPanel.Modules.Authentication.Configuration;

public sealed class AuthenticationOptions
{
    public const string SectionName = "Authentication";

    public string CookieName { get; set; } = "alegacy.auth";
    public int CookieLifetimeHours { get; set; } = 8;
    public bool SlidingExpiration { get; set; } = true;
    public int TrustedIpTtlDays { get; set; } = 30;
    public LoginLogOptions LoginLog { get; set; } = new();
    public AdminOptions Admin { get; set; } = new();
}

public sealed class LoginLogOptions
{
    public int MaxRetainedDays { get; set; } = 90;
}

public sealed class AdminOptions
{
    public string Username { get; set; } = "admin";
    public string PasswordFile { get; set; } = "/run/secrets/admin_password";
}
