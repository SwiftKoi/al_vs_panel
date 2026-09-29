using AlegacyWebPanel.Core.Authorization;
using AlegacyWebPanel.Modules.Users.Contracts;
using AlegacyWebPanel.Modules.Users.Exceptions;
using AlegacyWebPanel.Modules.Users.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using AlegacyWebPanel.Modules.Users.Persistence;

namespace AlegacyWebPanel.Users.UnitTests;

public sealed class UserServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly UserManager<IdentityUser> _userManager;
    private readonly UserService _service;

    public UserServiceTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
        foreach (var role in PanelRoles.All)
        {
            _db.Roles.Add(new IdentityRole(role) { NormalizedName = role.ToUpperInvariant() });
        }
        _db.SaveChanges();

        // Set up UserManager
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

        _service = new UserService(_userManager, NullLogger<UserService>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task ListUsers_returns_all_users()
    {
        var user1 = new IdentityUser { UserName = "user1", Email = "user1@example.com" };
        var user2 = new IdentityUser { UserName = "user2", Email = "user2@example.com" };
        await _userManager.CreateAsync(user1, "Password123!");
        await _userManager.CreateAsync(user2, "Password123!");

        var list = (await _service.ListUsersAsync(CancellationToken.None)).ToList();

        Assert.Equal(2, list.Count);
        Assert.Contains(list, u => u.Username == "user1");
        Assert.Contains(list, u => u.Username == "user2");
    }

    [Fact]
    public async Task CreateUser_succeeds_for_valid_request()
    {
        var response = await _service.CreateUserAsync(new CreateUserRequest("newadmin", "Password123!", PanelRoles.Moderator), CancellationToken.None);

        Assert.Equal("newadmin", response.Username);
        Assert.False(response.TwoFactorEnabled);
        Assert.Equal(PanelRoles.Moderator, response.Role);

        var user = await _userManager.FindByNameAsync("newadmin");
        Assert.NotNull(user);
        Assert.True(await _userManager.IsInRoleAsync(user, PanelRoles.Moderator));
    }

    [Fact]
    public async Task CreateUser_fails_if_username_already_exists()
    {
        var existing = new IdentityUser { UserName = "duplicate", Email = "duplicate@example.com" };
        await _userManager.CreateAsync(existing, "Password123!");

        await Assert.ThrowsAsync<UserAlreadyExistsException>(() =>
            _service.CreateUserAsync(new CreateUserRequest("duplicate", "Password123!", PanelRoles.Admin), CancellationToken.None));
    }

    [Fact]
    public async Task DeleteUser_succeeds_for_other_user()
    {
        var currentUser = new IdentityUser { UserName = "current", Email = "current@example.com" };
        var otherUser = new IdentityUser { UserName = "other", Email = "other@example.com" };
        await _userManager.CreateAsync(currentUser, "Password123!");
        await _userManager.CreateAsync(otherUser, "Password123!");

        await _service.DeleteUserAsync(otherUser.Id, currentUser.Id, CancellationToken.None);

        var deleted = await _userManager.FindByIdAsync(otherUser.Id);
        Assert.Null(deleted);
    }

    [Fact]
    public async Task DeleteUser_fails_on_self_deletion()
    {
        var currentUser = new IdentityUser { UserName = "current", Email = "current@example.com" };
        await _userManager.CreateAsync(currentUser, "Password123!");

        await Assert.ThrowsAsync<SelfDeletionException>(() =>
            _service.DeleteUserAsync(currentUser.Id, currentUser.Id, CancellationToken.None));
    }

    [Fact]
    public async Task CreateUser_rejects_unknown_role()
    {
        await Assert.ThrowsAsync<InvalidRoleException>(() =>
            _service.CreateUserAsync(new CreateUserRequest("someone", "Password123!", "Owner"), CancellationToken.None));
        Assert.Null(await _userManager.FindByNameAsync("someone"));
    }

    [Fact]
    public async Task ListUsers_includes_roles()
    {
        await CreateUserAsync("boss", PanelRoles.Admin);
        await CreateUserAsync("mod", PanelRoles.Moderator);

        var list = (await _service.ListUsersAsync(CancellationToken.None)).ToList();

        Assert.Equal(PanelRoles.Admin, list.Single(u => u.Username == "boss").Role);
        Assert.Equal(PanelRoles.Moderator, list.Single(u => u.Username == "mod").Role);
    }

    [Fact]
    public async Task ChangeRole_replaces_role_and_rotates_security_stamp()
    {
        var current = await CreateUserAsync("current", PanelRoles.Admin);
        var target = await CreateUserAsync("target", PanelRoles.Admin);
        var stampBefore = await _userManager.GetSecurityStampAsync(target);

        var response = await _service.ChangeRoleAsync(target.Id, PanelRoles.Moderator, current.Id, CancellationToken.None);

        Assert.Equal(PanelRoles.Moderator, response.Role);
        Assert.Equal([PanelRoles.Moderator], await _userManager.GetRolesAsync(target));
        Assert.NotEqual(stampBefore, await _userManager.GetSecurityStampAsync(target));
    }

    [Fact]
    public async Task ChangeRole_rejects_own_account()
    {
        var current = await CreateUserAsync("current", PanelRoles.Admin);
        await CreateUserAsync("other", PanelRoles.Admin);

        await Assert.ThrowsAsync<SelfRoleChangeException>(() =>
            _service.ChangeRoleAsync(current.Id, PanelRoles.Moderator, current.Id, CancellationToken.None));
    }

    [Fact]
    public async Task ChangeRole_rejects_unknown_role()
    {
        var current = await CreateUserAsync("current", PanelRoles.Admin);
        var target = await CreateUserAsync("target", PanelRoles.Moderator);

        await Assert.ThrowsAsync<InvalidRoleException>(() =>
            _service.ChangeRoleAsync(target.Id, "Owner", current.Id, CancellationToken.None));
    }

    [Fact]
    public async Task ChangeRole_rejects_demoting_the_last_admin()
    {
        var current = await CreateUserAsync("current", PanelRoles.Moderator);
        var admin = await CreateUserAsync("admin", PanelRoles.Admin);

        await Assert.ThrowsAsync<LastAdminException>(() =>
            _service.ChangeRoleAsync(admin.Id, PanelRoles.Moderator, current.Id, CancellationToken.None));
        Assert.True(await _userManager.IsInRoleAsync(admin, PanelRoles.Admin));
    }

    [Fact]
    public async Task DeleteUser_rejects_deleting_the_last_admin()
    {
        var current = await CreateUserAsync("current", PanelRoles.Moderator);
        var admin = await CreateUserAsync("admin", PanelRoles.Admin);

        await Assert.ThrowsAsync<LastAdminException>(() =>
            _service.DeleteUserAsync(admin.Id, current.Id, CancellationToken.None));
    }

    private async Task<IdentityUser> CreateUserAsync(string username, string role)
    {
        var user = new IdentityUser { UserName = username, Email = $"{username}@example.com" };
        await _userManager.CreateAsync(user, "Password123!");
        await _userManager.AddToRoleAsync(user, role);
        return user;
    }
}
