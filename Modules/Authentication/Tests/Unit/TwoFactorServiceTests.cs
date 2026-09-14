using System.Security.Cryptography;
using AlegacyWebPanel.Modules.Authentication.Configuration;
using AlegacyWebPanel.Modules.Authentication.Contracts;
using AlegacyWebPanel.Modules.Authentication.Exceptions;
using AlegacyWebPanel.Modules.Authentication.Persistence;
using AlegacyWebPanel.Modules.Authentication.Services;
using AlegacyWebPanel.Modules.Users.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AlegacyWebPanel.Authentication.UnitTests;

public sealed class TwoFactorServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly UserManager<IdentityUser> _userManager;
    private readonly EfTrustedIpRepository _trustedIpRepository;
    private readonly TwoFactorService _service;

    public TwoFactorServiceTests()
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

        // Register default token provider for Authenticator
        _userManager.RegisterTokenProvider(TokenOptions.DefaultAuthenticatorProvider, new AuthenticatorTokenProvider<IdentityUser>());

        _trustedIpRepository = new EfTrustedIpRepository(_db);

        var appOptions = Microsoft.Extensions.Options.Options.Create(new AuthenticationOptions
        {
            TrustedIpTtlDays = 30
        });

        _service = new TwoFactorService(_userManager, _trustedIpRepository, appOptions);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task TwoFactorEnabled_is_false_by_default()
    {
        var user = new IdentityUser { UserName = "test", Email = "test@example.com" };
        await _userManager.CreateAsync(user, "Password123!");

        var isEnabled = await _service.IsTwoFactorEnabledAsync(user.Id, CancellationToken.None);
        Assert.False(isEnabled);
    }

    [Fact]
    public async Task GetSetupDetails_generates_and_returns_secret_and_uri()
    {
        var user = new IdentityUser { UserName = "test", Email = "test@example.com" };
        await _userManager.CreateAsync(user, "Password123!");

        var setup = await _service.GetSetupDetailsAsync(user.Id, CancellationToken.None);

        Assert.NotEmpty(setup.SharedSecret);
        Assert.Contains("secret=" + setup.SharedSecret, setup.ProvisioningUri);
        Assert.Contains("AlegacyWebPanel:test", setup.ProvisioningUri);
    }

    [Fact]
    public async Task EnableTwoFactor_fails_with_invalid_code()
    {
        var user = new IdentityUser { UserName = "test", Email = "test@example.com" };
        await _userManager.CreateAsync(user, "Password123!");

        // Generate key
        await _service.GetSetupDetailsAsync(user.Id, CancellationToken.None);

        await Assert.ThrowsAsync<TwoFactorCodeInvalidException>(() =>
            _service.EnableTwoFactorAsync(user.Id, "000000", "127.0.0.1", CancellationToken.None));
    }

    [Fact]
    public async Task EnableTwoFactor_succeeds_with_valid_code_and_trusts_ip()
    {
        var user = new IdentityUser { UserName = "test", Email = "test@example.com" };
        await _userManager.CreateAsync(user, "Password123!");

        // Generate key first
        var setup = await _service.GetSetupDetailsAsync(user.Id, CancellationToken.None);

        // Generate valid TOTP code
        var code = GenerateTotpCode(setup.SharedSecret);

        await _service.EnableTwoFactorAsync(user.Id, code, "192.168.1.50", CancellationToken.None);

        // Assert 2FA is now enabled
        var isEnabled = await _service.IsTwoFactorEnabledAsync(user.Id, CancellationToken.None);
        Assert.True(isEnabled);

        // Assert IP is trusted
        var isIpTrusted = await _service.IsIpTrustedAsync(user.Id, "192.168.1.50", CancellationToken.None);
        Assert.True(isIpTrusted);
    }

    [Fact]
    public async Task DisableTwoFactor_clears_2fa_and_resets_secrets_and_trusted_ips()
    {
        var user = new IdentityUser { UserName = "test", Email = "test@example.com" };
        await _userManager.CreateAsync(user, "Password123!");

        // Set up 2FA
        var setup = await _service.GetSetupDetailsAsync(user.Id, CancellationToken.None);
        var code = GenerateTotpCode(setup.SharedSecret);
        await _service.EnableTwoFactorAsync(user.Id, code, "192.168.1.50", CancellationToken.None);

        // Disable
        await _service.DisableTwoFactorAsync(user.Id, CancellationToken.None);

        // Verify disabled
        Assert.False(await _service.IsTwoFactorEnabledAsync(user.Id, CancellationToken.None));
        
        // Verify IP no longer trusted
        Assert.False(await _service.IsIpTrustedAsync(user.Id, "192.168.1.50", CancellationToken.None));
    }

    [Fact]
    public async Task IsIpTrusted_cleans_up_expired_ips()
    {
        var user = new IdentityUser { UserName = "test", Email = "test@example.com" };
        await _userManager.CreateAsync(user, "Password123!");

        // Add expired trusted IP manually
        _db.UserTrustedIps.Add(new UserTrustedIp
        {
            UserId = user.Id,
            IpAddress = "10.0.0.1",
            CreatedAt = DateTime.UtcNow.AddDays(-31),
            ExpiresAt = DateTime.UtcNow.AddDays(-1)
        });
        await _db.SaveChangesAsync();

        // Checking the IP triggers cleanup
        var isTrusted = await _service.IsIpTrustedAsync(user.Id, "10.0.0.1", CancellationToken.None);
        Assert.False(isTrusted);

        // Verify it was physically deleted from the database
        var count = await _db.UserTrustedIps.CountAsync();
        Assert.Equal(0, count);
    }

    private static byte[] Base32Decode(string base32)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        base32 = base32.TrimEnd('=').ToUpperInvariant();
        if (string.IsNullOrEmpty(base32))
        {
            return Array.Empty<byte>();
        }

        var byteCount = base32.Length * 5 / 8;
        var result = new byte[byteCount];

        byte curByte = 0;
        var bitsRemaining = 8;
        var mask = 0;
        var arrayIndex = 0;

        foreach (var c in base32)
        {
            var value = alphabet.IndexOf(c);
            if (value < 0)
            {
                throw new FormatException("Invalid base32 character.");
            }

            if (bitsRemaining > 5)
            {
                mask = value << (bitsRemaining - 5);
                curByte = (byte)(curByte | mask);
                bitsRemaining -= 5;
            }
            else
            {
                mask = value >> (5 - bitsRemaining);
                curByte = (byte)(curByte | mask);
                result[arrayIndex++] = curByte;
                curByte = (byte)(value << (3 + bitsRemaining));
                bitsRemaining += 3;
            }
        }

        return result;
    }

    private static string GenerateTotpCode(string secret)
    {
        var key = Base32Decode(secret);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        var data = BitConverter.GetBytes(timestamp);
        
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(data);
        }

        using var hmac = new HMACSHA1(key);
        var hash = hmac.ComputeHash(data);

        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24)
                     | ((hash[offset + 1] & 0xFF) << 16)
                     | ((hash[offset + 2] & 0xFF) << 8)
                     | (hash[offset + 3] & 0xFF);

        var password = binary % 1000000;
        return password.ToString("D6");
    }
}
