using AlegacyWebPanel.Modules.Users.Configuration;
using AlegacyWebPanel.Modules.Users.Persistence;
using AlegacyWebPanel.Modules.Users.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AlegacyWebPanel.Modules.Users.Infrastructure;

public static class UsersModule
{
    public static IServiceCollection AddUsersModule(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddOptions<UsersOptions>()
            .Bind(configuration.GetSection("Authentication"))
            .ValidateOnStart();

        var databasePath = configuration.GetConnectionString("DefaultConnection")
                           ?? "Data Source=/var/lib/alegacy/data/alegacy.db";
        var databaseDirectory = Path.GetDirectoryName(databasePath.Replace("Data Source=", string.Empty, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(databaseDirectory))
        {
            Directory.CreateDirectory(databaseDirectory);
        }

        services.AddDbContext<AppDbContext>(options => options.UseSqlite(databasePath));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddIdentityCore<IdentityUser>(options =>
            {
                options.User.RequireUniqueEmail = false;
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddSignInManager()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

        services.AddScoped<IUserService, UserService>();
        services.AddScoped<AdminSeeder>();

        return services;
    }
}
