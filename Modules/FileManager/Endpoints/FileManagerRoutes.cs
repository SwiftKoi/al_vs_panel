using AlegacyWebPanel.Core.Endpoints;

namespace AlegacyWebPanel.Modules.FileManager.Endpoints;

public static class FileManagerRoutes
{
    public static IEndpointRouteBuilder MapFileManagerModule(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/servers").RequireAuthorization();

        group.MapGet("/{serverId}/files/{root}", FileManagerEndpoints.ListAsync);
        group.MapGet("/{serverId}/files/{root}/download", FileManagerEndpoints.DownloadAsync);
        group.MapGet("/{serverId}/files/{root}/content", FileManagerEndpoints.GetContentAsync);
        group.MapPut("/{serverId}/files/{root}/content", FileManagerEndpoints.SaveContentAsync).RequireAntiforgery();
        group.MapPost("/{serverId}/files/{root}/upload", FileManagerEndpoints.UploadAsync).RequireAntiforgery();
        group.MapPost("/{serverId}/files/{root}/mkdir", FileManagerEndpoints.CreateDirectoryAsync).RequireAntiforgery();
        group.MapPost("/{serverId}/files/{root}/rename", FileManagerEndpoints.RenameAsync).RequireAntiforgery();
        group.MapPost("/{serverId}/files/{root}/move", FileManagerEndpoints.MoveAsync).RequireAntiforgery();
        group.MapDelete("/{serverId}/files/{root}", FileManagerEndpoints.DeleteAsync).RequireAntiforgery();
        group.MapPost("/{serverId}/files/{root}/compress", FileManagerEndpoints.CompressAsync).RequireAntiforgery();
        group.MapPost("/{serverId}/files/{root}/extract", FileManagerEndpoints.ExtractAsync).RequireAntiforgery();
        group.MapGet("/operations/{taskId}", FileManagerEndpoints.GetOperationStatusAsync);

        return endpoints;
    }
}
