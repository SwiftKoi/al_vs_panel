using AlegacyWebPanel.Modules.RemoteOperations.Contracts;

namespace AlegacyWebPanel.Modules.RemoteOperations.Configuration;

public sealed class RemoteCommandConfig
{
    public string Target { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public List<string> Arguments { get; set; } = [];
    public string? User { get; set; }
}

public sealed class RemoteTargetConfig
{
    public ExecutionMode Mode { get; set; } = ExecutionMode.Ssh;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 22;
    public string Username { get; set; } = string.Empty;
    public string PrivateKeyFile { get; set; } = "/run/secrets/ssh_private_key";
    public string? PrivateKeyPassphraseFile { get; set; }
    public string HostKeyFingerprintSha256 { get; set; } = string.Empty;
}

public sealed class RemoteCommandOptions
{
    public Dictionary<string, RemoteTargetConfig> Targets { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, RemoteCommandConfig> Commands { get; set; } = new(StringComparer.Ordinal);
}
