namespace AlegacyWebPanel.Modules.Users.Persistence;

public sealed class UserTrustedIp
{
    public int Id { get; set; }
    public required string UserId { get; set; }
    public required string IpAddress { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}
