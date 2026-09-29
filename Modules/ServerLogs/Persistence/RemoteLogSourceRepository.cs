using System.Globalization;
using System.Text.Json;
using AlegacyWebPanel.Modules.RemoteOperations.Exceptions;
using AlegacyWebPanel.Modules.RemoteOperations.Services;
using AlegacyWebPanel.Modules.ServerLogs.Exceptions;

namespace AlegacyWebPanel.Modules.ServerLogs.Persistence;

public sealed class RemoteLogSourceRepository(IRemoteOperationsService remoteOperations) : ILogSourceRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<LogFileInfo>> ListAsync(string operation, CancellationToken cancellationToken)
    {
        var result = await remoteOperations.ExecuteAsync(operation, ["list"], cancellationToken);
        if (result.ExitStatus != 0)
        {
            throw new LogSourceException(HelperMessage(result.ErrorOutput));
        }

        try
        {
            return JsonSerializer.Deserialize<List<LogFileInfo>>(result.StandardOutput, JsonOptions) ?? [];
        }
        catch (JsonException exception)
        {
            throw new LogSourceException($"The log helper returned invalid JSON: {exception.Message}");
        }
    }

    public async Task<byte[]> ReadAsync(string operation, string path, long offset, int maximumBytes, CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        try
        {
            await remoteOperations.ExecuteBinaryAsync(
                operation,
                ["read", path, offset.ToString(CultureInfo.InvariantCulture), maximumBytes.ToString(CultureInfo.InvariantCulture)],
                null,
                output,
                cancellationToken);
        }
        catch (RemoteOperationFailedException exception)
        {
            throw new LogSourceException(HelperMessage(exception.ErrorOutput));
        }

        return output.ToArray();
    }

    private static string HelperMessage(string errorOutput)
    {
        var message = errorOutput.Trim();
        return message.StartsWith("Error: ", StringComparison.Ordinal) ? message["Error: ".Length..] : message;
    }
}
