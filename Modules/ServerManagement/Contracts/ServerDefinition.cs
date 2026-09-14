namespace AlegacyWebPanel.Modules.ServerManagement.Contracts;

public sealed record ServerDefinition(
    string Id,
    string Name,
    string Host,
    int Port,
    string Location,
    string StartOperation,
    string StopOperation,
    string RestartOperation,
    string StatusOperation,
    string ConsoleOperation,
    string LogsOperation,
    string MetricsOperation);
