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
        var response = await _service.CreateUserAsync(new CreateUserRequest("newadmin", "Password123!"), CancellationToken.None);

        Assert.Equal("newadmin", response.Username);
        Assert.False(response.TwoFactorEnabled);

        var user = await _userManager.FindByNameAsync("newadmin");
        Assert.NotNull(user);
    }

    [Fact]
    public async Task CreateUser_fails_if_username_already_exists()
    {
        var existing = new IdentityUser { UserName = "duplicate", Email = "duplicate@example.com" };
        await _userManager.CreateAsync(existing, "Password123!");

        await Assert.ThrowsAsync<UserAlreadyExistsException>(() =>
            _service.CreateUserAsync(new CreateUserRequest("duplicate", "Password123!"), CancellationToken.None));
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
}
