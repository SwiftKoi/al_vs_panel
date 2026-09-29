using Microsoft.AspNetCore.Authorization;

namespace AlegacyWebPanel.Core.Authorization;

/// <summary>
/// Authorization policies for browser routes. The default policy is <see cref="Admin"/>,
/// so a plain <c>RequireAuthorization()</c> is admin-only; routes moderators may use opt in
/// with <c>RequireAuthorization(PanelPolicies.Staff)</c>.
/// </summary>
public static class PanelPolicies
{
    public const string Admin = "PanelAdmin";
    public const string Staff = "PanelStaff";

    /// <summary>Any signed-in cookie, even one without a role yet. Only for session, refresh and logout.</summary>
    public const string SignedIn = "PanelSignedIn";

    public static AuthorizationPolicy AdminPolicy { get; } = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .RequireRole(PanelRoles.Admin)
        .Build();

    public static AuthorizationPolicy StaffPolicy { get; } = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .RequireRole(PanelRoles.Admin, PanelRoles.Moderator)
        .Build();

    /// <summary>Registers both policies and makes <see cref="Admin"/> the default.</summary>
    public static void Configure(AuthorizationOptions options)
    {
        options.AddPolicy(Admin, AdminPolicy);
        options.AddPolicy(Staff, StaffPolicy);
        options.AddPolicy(SignedIn, policy => policy.RequireAuthenticatedUser());
        options.DefaultPolicy = AdminPolicy;
    }
}
