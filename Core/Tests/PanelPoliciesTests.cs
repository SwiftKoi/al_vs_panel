using System.Security.Claims;
using AlegacyWebPanel.Core.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace AlegacyWebPanel.Core.Tests;

public sealed class PanelPoliciesTests
{
    private static readonly IAuthorizationService Authorization = new ServiceCollection()
        .AddLogging()
        .AddAuthorization(PanelPolicies.Configure)
        .BuildServiceProvider()
        .GetRequiredService<IAuthorizationService>();

    [Theory]
    [InlineData(PanelRoles.Admin, PanelPolicies.Admin, true)]
    [InlineData(PanelRoles.Moderator, PanelPolicies.Admin, false)]
    [InlineData(null, PanelPolicies.Admin, false)]
    [InlineData(PanelRoles.Admin, PanelPolicies.Staff, true)]
    [InlineData(PanelRoles.Moderator, PanelPolicies.Staff, true)]
    [InlineData(null, PanelPolicies.Staff, false)]
    [InlineData(null, PanelPolicies.SignedIn, true)]
    public async Task Policies_accept_only_their_roles(string? role, string policy, bool expected)
    {
        var result = await Authorization.AuthorizeAsync(SignedIn(role), policy);

        Assert.Equal(expected, result.Succeeded);
    }

    [Fact]
    public async Task Default_policy_is_admin_only()
    {
        var provider = new ServiceCollection().AddLogging().AddAuthorization(PanelPolicies.Configure)
            .BuildServiceProvider().GetRequiredService<IAuthorizationPolicyProvider>();
        var defaultPolicy = await provider.GetDefaultPolicyAsync();

        Assert.True((await Authorization.AuthorizeAsync(SignedIn(PanelRoles.Admin), defaultPolicy)).Succeeded);
        Assert.False((await Authorization.AuthorizeAsync(SignedIn(PanelRoles.Moderator), defaultPolicy)).Succeeded);
    }

    [Fact]
    public async Task Anonymous_users_are_rejected_by_every_policy()
    {
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

        foreach (var policy in new[] { PanelPolicies.Admin, PanelPolicies.Staff, PanelPolicies.SignedIn })
        {
            Assert.False((await Authorization.AuthorizeAsync(anonymous, policy)).Succeeded);
        }
    }

    private static ClaimsPrincipal SignedIn(string? role)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, "user") };
        if (role is not null)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }
}
