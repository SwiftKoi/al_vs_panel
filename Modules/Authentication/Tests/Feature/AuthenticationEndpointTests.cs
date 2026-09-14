using AlegacyWebPanel.Core.Errors;
using AlegacyWebPanel.Modules.Authentication.Contracts;
using AlegacyWebPanel.Modules.Authentication.Endpoints;
using AlegacyWebPanel.Modules.Authentication.Exceptions;
using AlegacyWebPanel.Modules.Authentication.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace AlegacyWebPanel.Authentication.FeatureTests;

public sealed class AuthenticationEndpointTests
{
    [Fact]
    public async Task Login_coordinates_service_and_session_when_2fa_disabled()
    {
        var session = new FakeAuthenticationSession();
        var twoFactorService = new FakeTwoFactorService { IsEnabled = false };
        var loginLog = new FakeLoginLogService();
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("127.0.0.1");

        var result = await AuthenticationEndpoints.LoginAsync(
            new LoginRequest("admin", "correct"),
            new FakeAuthenticationService(),
            twoFactorService,
            session,
            loginLog,
            httpContext,
            CancellationToken.None);

        var response = Assert.IsType<Ok<LoginResponse>>(result);
        Assert.True(response.Value!.Authenticated);
        Assert.False(response.Value.RequiresTwoFactor);
        Assert.Equal("1", session.SignedInUser!.Id);
        Assert.Equal("127.0.0.1", twoFactorService.TrustedIpAddress);
        var recorded = Assert.Single(loginLog.Recorded);
        Assert.Equal("admin", recorded.Username);
        Assert.True(recorded.Succeeded);
    }

    [Fact]
    public async Task Login_challenges_2fa_when_enabled_and_ip_untrusted()
    {
        var session = new FakeAuthenticationSession();
        var twoFactorService = new FakeTwoFactorService { IsEnabled = true, IsIpTrusted = false };
        var loginLog = new FakeLoginLogService();
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("192.168.1.100");

        var result = await AuthenticationEndpoints.LoginAsync(
            new LoginRequest("admin", "correct"),
            new FakeAuthenticationService(),
            twoFactorService,
            session,
            loginLog,
            httpContext,
            CancellationToken.None);

        var response = Assert.IsType<Ok<LoginResponse>>(result);
        Assert.False(response.Value!.Authenticated);
        Assert.True(response.Value.RequiresTwoFactor);
        Assert.Null(session.SignedInUser);
        Assert.Equal("1", session.TwoFactorUser!.Id);
        Assert.Empty(loginLog.Recorded);
    }

    [Fact]
    public async Task Login_bypasses_2fa_when_enabled_and_ip_trusted()
    {
        var session = new FakeAuthenticationSession();
        var twoFactorService = new FakeTwoFactorService { IsEnabled = true, IsIpTrusted = true };
        var loginLog = new FakeLoginLogService();
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("192.168.1.100");

        var result = await AuthenticationEndpoints.LoginAsync(
            new LoginRequest("admin", "correct"),
            new FakeAuthenticationService(),
            twoFactorService,
            session,
            loginLog,
            httpContext,
            CancellationToken.None);

        var response = Assert.IsType<Ok<LoginResponse>>(result);
        Assert.True(response.Value!.Authenticated);
        Assert.False(response.Value.RequiresTwoFactor);
        Assert.Equal("1", session.SignedInUser!.Id);
        Assert.Equal("192.168.1.100", twoFactorService.TrustedIpAddress);
        Assert.True(Assert.Single(loginLog.Recorded).Succeeded);
    }

    [Fact]
    public async Task Login_translates_domain_failure_to_http_exception_and_records_failure()
    {
        var loginLog = new FakeLoginLogService();
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("127.0.0.1");

        await Assert.ThrowsAsync<HttpException>(() => AuthenticationEndpoints.LoginAsync(
            new LoginRequest("admin", "wrong"),
            new FailingAuthenticationService(),
            new FakeTwoFactorService(),
            new FakeAuthenticationSession(),
            loginLog,
            httpContext,
            CancellationToken.None));

        var recorded = Assert.Single(loginLog.Recorded);
        Assert.Equal("admin", recorded.Username);
        Assert.False(recorded.Succeeded);
        Assert.Equal("127.0.0.1", recorded.IpAddress);
    }

    [Fact]
    public async Task LoginTwoFactor_verifies_code_and_completes_login()
    {
        var session = new FakeAuthenticationSession { TwoFactorUser = new AuthenticatedUser("1", "admin") };
        var twoFactorService = new FakeTwoFactorService();
        var loginLog = new FakeLoginLogService();
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("127.0.0.1");

        var result = await AuthenticationEndpoints.LoginTwoFactorAsync(
            new TwoFactorLoginRequest("123456"),
            twoFactorService,
            session,
            loginLog,
            httpContext,
            CancellationToken.None);

        var response = Assert.IsType<Ok<LoginResponse>>(result);
        Assert.True(response.Value!.Authenticated);
        Assert.Equal("1", session.SignedInUser!.Id);
        Assert.True(session.TwoFactorCookieCleared);
        Assert.Equal("127.0.0.1", twoFactorService.TrustedIpAddress);
        Assert.Equal("123456", twoFactorService.VerifiedCode);
        Assert.True(Assert.Single(loginLog.Recorded).Succeeded);
    }

    [Fact]
    public async Task LoginTwoFactor_translates_invalid_code_to_http_exception_and_records_failure()
    {
        var session = new FakeAuthenticationSession { TwoFactorUser = new AuthenticatedUser("1", "admin") };
        var twoFactorService = new FakeTwoFactorService();
        var loginLog = new FakeLoginLogService();
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("10.0.0.5");

        await Assert.ThrowsAsync<HttpException>(() => AuthenticationEndpoints.LoginTwoFactorAsync(
            new TwoFactorLoginRequest("fail"),
            twoFactorService,
            session,
            loginLog,
            httpContext,
            CancellationToken.None));

        var recorded = Assert.Single(loginLog.Recorded);
        Assert.Equal("admin", recorded.Username);
        Assert.False(recorded.Succeeded);
        Assert.Equal("10.0.0.5", recorded.IpAddress);
    }

    [Fact]
    public async Task RecentLogins_coordinates_service_and_returns_page()
    {
        var loginLog = new FakeLoginLogService
        {
            Page = new LoginLogPageDto(
                new[]
                {
                    new LoginEventDto(2, "admin", new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc), "192.168.1.100", true),
                    new LoginEventDto(1, "nikita", new DateTime(2026, 7, 31, 9, 0, 0, DateTimeKind.Utc), "10.0.0.45", false)
                },
                Total: 12,
                Offset: 0,
                Limit: 2)
        };

        var result = await AuthenticationEndpoints.RecentLoginsAsync(loginLog, 1, 2, CancellationToken.None);

        var response = Assert.IsType<Ok<LoginLogPageDto>>(result);
        Assert.Equal(2, response.Value!.Items.Count);
        Assert.Equal(12, response.Value.Total);
        Assert.Equal(1, loginLog.RequestedPage);
        Assert.Equal(2, loginLog.RequestedPageSize);
    }

    [Fact]
    public async Task RecentLogins_defaults_page_and_page_size()
    {
        var loginLog = new FakeLoginLogService { Page = new LoginLogPageDto([], Total: 0, Offset: 0, Limit: 10) };

        await AuthenticationEndpoints.RecentLoginsAsync(loginLog, null, null, CancellationToken.None);

        Assert.Equal(1, loginLog.RequestedPage);
        Assert.Equal(10, loginLog.RequestedPageSize);
    }

    [Fact]
    public async Task GetTwoFactorSetup_coordinates_service()
    {
        var session = new FakeAuthenticationSession { CurrentUser = new AuthenticatedUser("1", "admin") };
        var twoFactorService = new FakeTwoFactorService { SetupSecret = "SECRETKEY", SetupUri = "otpauth://123" };

        var result = await AuthenticationEndpoints.GetTwoFactorSetupAsync(
            session,
            twoFactorService,
            CancellationToken.None);

        var response = Assert.IsType<Ok<TwoFactorSetupResponse>>(result);
        Assert.Equal("SECRETKEY", response.Value!.SharedSecret);
        Assert.Equal("otpauth://123", response.Value!.ProvisioningUri);
        Assert.True(twoFactorService.SetupCalled);
    }

    [Fact]
    public async Task EnableTwoFactor_verifies_and_enables()
    {
        var session = new FakeAuthenticationSession { CurrentUser = new AuthenticatedUser("1", "admin") };
        var twoFactorService = new FakeTwoFactorService { IsEnabled = false };
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("127.0.0.1");

        var result = await AuthenticationEndpoints.EnableTwoFactorAsync(
            new EnableTwoFactorRequest("123456"),
            session,
            twoFactorService,
            httpContext,
            CancellationToken.None);

        var response = Assert.IsAssignableFrom<IValueHttpResult>(result);
        Assert.NotNull(response.Value);
        Assert.True(twoFactorService.EnableCalled);
        Assert.True(twoFactorService.IsEnabled);
        Assert.Equal("127.0.0.1", twoFactorService.TrustedIpAddress);
    }

    [Fact]
    public async Task DisableTwoFactor_coordinates_service()
    {
        var session = new FakeAuthenticationSession { CurrentUser = new AuthenticatedUser("1", "admin") };
        var twoFactorService = new FakeTwoFactorService { IsEnabled = true };

        var result = await AuthenticationEndpoints.DisableTwoFactorAsync(
            session,
            twoFactorService,
            CancellationToken.None);

        var response = Assert.IsAssignableFrom<IValueHttpResult>(result);
        Assert.NotNull(response.Value);
        Assert.True(twoFactorService.DisableCalled);
        Assert.False(twoFactorService.IsEnabled);
    }

    [Fact]
    public async Task Session_returns_authenticated_user()
    {
        var result = await AuthenticationEndpoints.SessionAsync(
            new FakeAuthenticationSession { CurrentUser = new AuthenticatedUser("1", "admin") },
            CancellationToken.None);

        var response = Assert.IsType<Ok<AuthenticationSessionResponse>>(result);
        Assert.Equal("admin", response.Value!.User!.Username);
    }

    [Fact]
    public async Task Session_rejects_missing_session()
    {
        await Assert.ThrowsAsync<HttpException>(() => AuthenticationEndpoints.SessionAsync(
            new FakeAuthenticationSession(),
            CancellationToken.None));
    }

    private sealed class FakeLoginLogService : ILoginLogService
    {
        public sealed record RecordedLogin(string Username, string IpAddress, bool Succeeded);

        public List<RecordedLogin> Recorded { get; } = [];
        public LoginLogPageDto Page { get; init; } = new([], 0, 0, 0);
        public int? RequestedPage { get; private set; }
        public int? RequestedPageSize { get; private set; }

        public Task RecordLoginAsync(string? username, string ipAddress, bool succeeded, CancellationToken cancellationToken)
        {
            Recorded.Add(new RecordedLogin(username ?? string.Empty, ipAddress, succeeded));
            return Task.CompletedTask;
        }

        public Task<LoginLogPageDto> GetRecentLoginsAsync(int page, int pageSize, CancellationToken cancellationToken)
        {
            RequestedPage = page;
            RequestedPageSize = pageSize;
            return Task.FromResult(Page);
        }
    }

    private sealed class FakeAuthenticationService : IAuthenticationService
    {
        public Task<AuthenticatedUser> AuthenticateAsync(LoginCommand command, CancellationToken cancellationToken) =>
            Task.FromResult(new AuthenticatedUser("1", command.Username));
    }

    private sealed class FailingAuthenticationService : IAuthenticationService
    {
        public Task<AuthenticatedUser> AuthenticateAsync(LoginCommand command, CancellationToken cancellationToken) =>
            throw new AuthenticationFailedException();
    }

    private sealed class FakeAuthenticationSession : IAuthenticationSession
    {
        public AuthenticatedUser? SignedInUser { get; private set; }
        public AuthenticatedUser? CurrentUser { get; init; }
        public AuthenticatedUser? TwoFactorUser { get; set; }
        public bool TwoFactorCookieCleared { get; private set; }

        public Task<AuthenticatedUser?> GetCurrentUserAsync(CancellationToken cancellationToken) =>
            Task.FromResult(CurrentUser);

        public Task SignInAsync(AuthenticatedUser user, CancellationToken cancellationToken)
        {
            SignedInUser = user;
            return Task.CompletedTask;
        }

        public Task<bool> RefreshAsync(CancellationToken cancellationToken) => Task.FromResult(CurrentUser is not null);

        public Task SignOutAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SignInTwoFactorAsync(AuthenticatedUser user, CancellationToken cancellationToken)
        {
            TwoFactorUser = user;
            return Task.CompletedTask;
        }

        public Task<AuthenticatedUser?> GetTwoFactorUserAsync(CancellationToken cancellationToken) =>
            Task.FromResult(TwoFactorUser);

        public Task ClearTwoFactorCookieAsync(CancellationToken cancellationToken)
        {
            TwoFactorCookieCleared = true;
            TwoFactorUser = null;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTwoFactorService : ITwoFactorService
    {
        public bool IsEnabled { get; set; }
        public bool IsIpTrusted { get; set; }
        public string? TrustedIpUserId { get; private set; }
        public string? TrustedIpAddress { get; private set; }
        public string? VerifiedCodeUserId { get; private set; }
        public string? VerifiedCode { get; private set; }
        public bool SetupCalled { get; private set; }
        public bool EnableCalled { get; private set; }
        public bool DisableCalled { get; private set; }
        public string? SetupSecret { get; set; } = "SECRET";
        public string? SetupUri { get; set; } = "otpauth://...";

        public Task<bool> IsTwoFactorEnabledAsync(string userId, CancellationToken cancellationToken) =>
            Task.FromResult(IsEnabled);

        public Task<bool> IsIpTrustedAsync(string userId, string ipAddress, CancellationToken cancellationToken) =>
            Task.FromResult(IsIpTrusted);

        public Task TrustIpAsync(string userId, string ipAddress, CancellationToken cancellationToken)
        {
            TrustedIpUserId = userId;
            TrustedIpAddress = ipAddress;
            return Task.CompletedTask;
        }

        public Task<TwoFactorSetupResponse> GetSetupDetailsAsync(string userId, CancellationToken cancellationToken)
        {
            SetupCalled = true;
            return Task.FromResult(new TwoFactorSetupResponse(SetupSecret!, SetupUri!));
        }

        public Task EnableTwoFactorAsync(string userId, string code, string clientIp, CancellationToken cancellationToken)
        {
            EnableCalled = true;
            if (code == "fail") throw new TwoFactorCodeInvalidException();
            IsEnabled = true;
            TrustedIpUserId = userId;
            TrustedIpAddress = clientIp;
            return Task.CompletedTask;
        }

        public Task DisableTwoFactorAsync(string userId, CancellationToken cancellationToken)
        {
            DisableCalled = true;
            IsEnabled = false;
            return Task.CompletedTask;
        }

        public Task VerifyTwoFactorCodeAsync(string userId, string code, CancellationToken cancellationToken)
        {
            VerifiedCodeUserId = userId;
            VerifiedCode = code;
            if (code == "fail") throw new TwoFactorCodeInvalidException();
            return Task.CompletedTask;
        }
    }
}
