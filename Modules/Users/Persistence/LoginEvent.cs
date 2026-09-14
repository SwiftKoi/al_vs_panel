namespace AlegacyWebPanel.Modules.Users.Persistence;

public sealed class LoginEvent
{
    public int Id { get; set; }
    public required string Username { get; set; }
    public required string IpAddress { get; set; }
    public bool Succeeded { get; set; }
    public DateTime OccurredAt { get; set; }
}
