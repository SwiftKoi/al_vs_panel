namespace AlegacyWebPanel.Modules.Users.Configuration;

public sealed class UsersOptions
{
    public AdminOptions Admin { get; set; } = new();
}

public sealed class AdminOptions
{
    public string Username { get; set; } = "admin";
    public string PasswordFile { get; set; } = "/run/secrets/admin_password";
}
