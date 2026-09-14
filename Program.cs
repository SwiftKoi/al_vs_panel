using AlegacyWebPanel.Core.Abstractions;
using AlegacyWebPanel.Core.Endpoints;
using AlegacyWebPanel.Core.Infrastructure.Secrets;
using AlegacyWebPanel.Modules.Authentication.Endpoints;
using AlegacyWebPanel.Modules.Authentication.Infrastructure;
using AlegacyWebPanel.Modules.Authentication.Services;
using AlegacyWebPanel.Modules.Users.Endpoints;
using AlegacyWebPanel.Modules.Users.Infrastructure;
using AdminSeeder = AlegacyWebPanel.Modules.Users.Services.AdminSeeder;
using AlegacyWebPanel.Modules.RemoteOperations.Endpoints;
using AlegacyWebPanel.Modules.RemoteOperations.Infrastructure;
using AlegacyWebPanel.Modules.ServerManagement.Endpoints;
using AlegacyWebPanel.Modules.ServerManagement.Infrastructure;
using AlegacyWebPanel.Modules.FileManager.Endpoints;
using AlegacyWebPanel.Modules.FileManager.Infrastructure;
using AlegacyWebPanel.Modules.Logging.Endpoints;
using AlegacyWebPanel.Modules.Logging.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

// Production traffic enters through the gateway container. The application
// service is not published, so forwarded headers can only arrive from that
// private Docker network.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
        | ForwardedHeaders.XForwardedProto
        | ForwardedHeaders.XForwardedHost;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddLoggingModule(builder.Configuration);
builder.Services.AddUsersModule(builder.Configuration, builder.Environment);
builder.Services.AddAuthenticationModule(builder.Configuration, builder.Environment);

var dataProtectionPath = builder.Configuration["DataProtection:KeysPath"] ?? "/var/lib/alegacy/keys";
Directory.CreateDirectory(dataProtectionPath);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionPath))
    .SetApplicationName("AlegacyWebPanel");

builder.Services.AddSingleton<ISecretReader, SecretFileReader>();
builder.Services.AddRemoteOperationsModule(builder.Configuration);
builder.Services.AddServerManagementModule(builder.Configuration);
builder.Services.AddFileManagerModule(builder.Configuration);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<AdminSeeder>().SeedAsync();
}

app.UseForwardedHeaders();
app.UseCoreExceptionHandling();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapHealthEndpoints();
app.MapAuthenticationModule();
app.MapUsersModule();
app.MapRemoteOperationsModule();
app.MapServerManagementModule();
app.MapFileManagerModule();
app.MapLoggingModule();

app.Run();
