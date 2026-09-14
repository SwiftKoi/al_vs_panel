namespace AlegacyWebPanel.Modules.RemoteOperations.Contracts;

public enum ExecutionMode
{
    Ssh,
    Local
}

public enum RemoteOperationOutputKind
{
    StandardOutput,
    StandardError,
    Completed
}

public sealed record ExecuteRemoteOperationResponse(int ExitStatus, string StandardOutput, string ErrorOutput);

public sealed record RemoteOperationOutput(
    RemoteOperationOutputKind Kind,
    string? Data = null,
    int? ExitStatus = null);

public sealed record RemoteExecutionTargetDefinition(
    string Name,
    ExecutionMode Mode,
    string Host = "",
    int Port = 22,
    string Username = "",
    string PrivateKeyFile = "/run/secrets/ssh_private_key",
    string? PrivateKeyPassphraseFile = null,
    string HostKeyFingerprintSha256 = "");

public sealed record RemoteCommandDefinition(
    string Operation,
    string Command,
    RemoteExecutionTargetDefinition Target,
    string? User = null,
    IReadOnlyList<string>? Arguments = null);
