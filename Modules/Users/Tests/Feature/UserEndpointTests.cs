using System.Security.Claims;
using AlegacyWebPanel.Core.Errors;
using AlegacyWebPanel.Modules.Authentication.Contracts;
using AlegacyWebPanel.Modules.Users.Contracts;
using AlegacyWebPanel.Modules.Users.Endpoints;
using AlegacyWebPanel.Modules.Users.Exceptions;
using AlegacyWebPanel.Modules.Users.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using AlegacyWebPanel.Modules.Authentication.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using AlegacyWebPanel.Modules.Users.Persistence;

namespace AlegacyWebPanel.Users.FeatureTests;

public sealed class UserEndpointTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly UserManager<IdentityUser> _userManager;

    public UserEndpointTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        var userStore = new Microsoft.AspNetCore.Identity.EntityFrameworkCore.UserStore<IdentityUser>(_db);
        var passwordHasher = new PasswordHasher<IdentityUser>();
        var userValidators = new List<IUserValidator<IdentityUser>> { new UserValidator<IdentityUser>() };
        var passwordValidators = new List<IPasswordValidator<IdentityUser>> { new PasswordValidator<IdentityUser>() };
        var keyNormalizer = new UpperInvariantLookupNormalizer();
        var errors = new IdentityErrorDescriber();
        var logger = NullLogger<UserManager<IdentityUser>>.Instance;

        _userManager = new UserManager<IdentityUser>(
            userStore,
            null,
            passwordHasher,
            userValidators,
            passwordValidators,
            keyNormalizer,
            errors,
            null,
            logger);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task List_coordinates_service()
    {
        var service = new FakeUserService
        {
            Users = new[]
            {
                new UserResponse("1", "admin1", false),
                new UserResponse("2", "admin2", true)
            }
        };

        var result = await UserEndpoints.ListAsync(service, CancellationToken.None);

        var response = Assert.IsType<Ok<IEnumerable<UserResponse>>>(result);
        var list = response.Value.ToList();
        Assert.Equal(2, list.Count);
        Assert.Equal("admin1", list[0].Username);
        Assert.Equal("admin2", list[1].Username);
    }

    [Fact]
    public async Task Create_coordinates_service_and_returns_created()
    {
        var service = new FakeUserService();
        var request = new CreateUserRequest("newadmin", "Password123!");

        var result = await UserEndpoints.CreateAsync(request, service, CancellationToken.None);

        var response = Assert.IsType<Created<UserResponse>>(result);
        Assert.Equal("newadmin", response.Value!.Username);
        Assert.Equal("/api/users/123", response.Location);
    }

    [Fact]
    public async Task Create_translates_domain_exceptions()
    {
        var service = new FakeUserService { FailOnCreate = true };
        var request = new CreateUserRequest("duplicate", "Password123!");

        await Assert.ThrowsAsync<HttpException>(() =>
            UserEndpoints.CreateAsync(request, service, CancellationToken.None));
    }

    [Fact]
    public async Task Delete_verifies_identity_and_deletes()
    {
        var currentUser = new IdentityUser { UserName = "current", Email = "current@example.com" };
        await _userManager.CreateAsync(currentUser, "Password123!");

        var service = new FakeUserService();
        var session = new FakeAuthenticationSession { CurrentUser = new AuthenticatedUser(currentUser.Id, currentUser.UserName) };

        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, currentUser.Id), new Claim(ClaimTypes.Name, currentUser.UserName) };
        var claimsPrincipal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));

        var result = await UserEndpoints.DeleteAsync("target_id", service, _userManager, claimsPrincipal, CancellationToken.None);

        Assert.IsType<NoContent>(result);
        Assert.Equal("target_id", service.DeletedUserId);
        Assert.Equal(currentUser.Id, service.CurrentUserId);
    }

    [Fact]
    public async Task Delete_rejects_anonymous_user()
    {
        var service = new FakeUserService();
        var claimsPrincipal = new ClaimsPrincipal(new ClaimsIdentity()); // Not authenticated/empty

        await Assert.ThrowsAsync<HttpException>(() =>
            UserEndpoints.DeleteAsync("target_id", service, _userManager, claimsPrincipal, CancellationToken.None));
    }

    private sealed class FakeUserService : IUserService
    {
        public IEnumerable<UserResponse> Users { get; set; } = Array.Empty<UserResponse>();
        public string? DeletedUserId { get; private set; }
        public string? CurrentUserId { get; private set; }
        public bool FailOnCreate { get; set; }

        public Task<IEnumerable<UserResponse>> ListUsersAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Users);

        public Task<UserResponse> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken)
        {
            if (FailOnCreate)
            {
                throw new UserAlreadyExistsException(request.Username);
            }
            return Task.FromResult(new UserResponse("123", request.Username, false));
        }

        public Task DeleteUserAsync(string userId, string currentUserId, CancellationToken cancellationToken)
        {
            DeletedUserId = userId;
            CurrentUserId = currentUserId;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAuthenticationSession : IAuthenticationSession
    {
        public AuthenticatedUser? CurrentUser { get; set; }

        public Task<AuthenticatedUser?> GetCurrentUserAsync(CancellationToken cancellationToken) =>
            Task.FromResult(CurrentUser);

        public Task SignInAsync(AuthenticatedUser user, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<bool> RefreshAsync(CancellationToken cancellationToken) => Task.FromResult(true);
        public Task SignOutAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SignInTwoFactorAsync(AuthenticatedUser user, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<AuthenticatedUser?> GetTwoFactorUserAsync(CancellationToken cancellationToken) => Task.FromResult<AuthenticatedUser?>(null);
        public Task ClearTwoFactorCookieAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
