namespace AlegacyWebPanel.Modules.ServerManagement.Configuration;

public sealed class ServerManagementOptions
{
    public const string SectionName = "Servers";

    public int MaximumCommandLength { get; set; } = 512;
    public List<ServerInstanceConfig> Instances { get; set; } = [];
}

public sealed class ServerInstanceConfig
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public string Location { get; set; } = string.Empty;
    public string StartOperation { get; set; } = string.Empty;
    public string StopOperation { get; set; } = string.Empty;
    public string RestartOperation { get; set; } = string.Empty;
    public string StatusOperation { get; set; } = string.Empty;
    public string ConsoleOperation { get; set; } = string.Empty;
    public string LogsOperation { get; set; } = string.Empty;
    public string MetricsOperation { get; set; } = string.Empty;
}
