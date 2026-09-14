using AlegacyWebPanel.Modules.RemoteOperations.Configuration;
using AlegacyWebPanel.Modules.RemoteOperations.Contracts;
using AlegacyWebPanel.Modules.RemoteOperations.Persistence;
using AlegacyWebPanel.Modules.RemoteOperations.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AlegacyWebPanel.Modules.RemoteOperations.Infrastructure;

public static class RemoteOperationsModule
{
    public static IServiceCollection AddRemoteOperationsModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<RemoteCommandOptions>()
            .Bind(configuration.GetSection("Remote"))
            .Validate(ValidateOptions, "Remote targets and commands must be valid.")
            .ValidateOnStart();

        services.PostConfigure<RemoteCommandOptions>(options =>
        {
            var workspacePath = configuration["WorkspacePath"];
            if (string.IsNullOrEmpty(workspacePath)) return;

            foreach (var command in options.Commands.Values)
            {
                if (command.Command.Contains("%WorkspacePath%"))
                {
                    command.Command = command.Command.Replace("%WorkspacePath%", workspacePath);
                }
                for (int i = 0; i < command.Arguments.Count; i++)
                {
                    if (command.Arguments[i].Contains("%WorkspacePath%"))
                    {
                        command.Arguments[i] = command.Arguments[i].Replace("%WorkspacePath%", workspacePath);
                    }
                }
            }
        });

        services.AddSingleton<IRemoteCommandRepository, ConfigurationRemoteCommandRepository>();
        services.AddSingleton<SshRemoteConnection>();
        services.AddSingleton<LocalRemoteConnection>();
        services.AddSingleton<IRemoteConnectionFactory, RemoteConnectionFactory>();
        services.AddScoped<IRemoteOperationsService, RemoteOperationsService>();
        return services;
    }

    private static bool ValidateOptions(RemoteCommandOptions options)
    {
        if (options.Targets.Any(pair =>
                string.IsNullOrWhiteSpace(pair.Key) ||
                pair.Value is null ||
                (pair.Value.Mode == ExecutionMode.Ssh &&
                 (string.IsNullOrWhiteSpace(pair.Value.Host) ||
                  pair.Value.Port is < 1 or > 65535 ||
                  string.IsNullOrWhiteSpace(pair.Value.Username) ||
                  string.IsNullOrWhiteSpace(pair.Value.PrivateKeyFile) ||
                  string.IsNullOrWhiteSpace(pair.Value.HostKeyFingerprintSha256)))))
        {
            return false;
        }

        return options.Commands.All(pair =>
            !string.IsNullOrWhiteSpace(pair.Key) &&
            pair.Value is not null &&
            !string.IsNullOrWhiteSpace(pair.Value.Command) &&
            !string.IsNullOrWhiteSpace(pair.Value.Target) &&
            options.Targets.ContainsKey(pair.Value.Target));
    }
}
