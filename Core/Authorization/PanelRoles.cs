namespace AlegacyWebPanel.Core.Authorization;

/// <summary>Browser panel account roles. Every user has exactly one.</summary>
public static class PanelRoles
{
    public const string Admin = "Admin";
    public const string Moderator = "Moderator";

    public static IReadOnlyList<string> All { get; } = [Admin, Moderator];

    public static bool IsKnown(string? role) => role is not null && All.Contains(role, StringComparer.Ordinal);
}
