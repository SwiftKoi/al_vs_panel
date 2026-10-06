using AlegacyWebPanel.Core.Auditing;
using AlegacyWebPanel.Core.Endpoints;

namespace AlegacyWebPanel.Modules.FileManager.Endpoints;

public static class FileManagerRoutes
{
    public static IEndpointRouteBuilder MapFileManagerModule(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/servers").RequireAuthorization();

        group.MapGet("/{serverId}/files", FileManagerEndpoints.RootsAsync);
        group.MapGet("/{serverId}/files/{root}", FileManagerEndpoints.ListAsync);
        group.MapGet("/{serverId}/files/{root}/size", FileManagerEndpoints.MeasureAsync);
        group.MapGet("/{serverId}/files/{root}/search", FileManagerEndpoints.SearchAsync);
        group.MapGet("/{serverId}/files/{root}/download", FileManagerEndpoints.DownloadAsync);
        group.MapGet("/{serverId}/files/{root}/content", FileManagerEndpoints.GetContentAsync);
        group.MapPut("/{serverId}/files/{root}/content", FileManagerEndpoints.SaveContentAsync).RequireAntiforgery().Audited(AuditCategories.Files, "save");
        group.MapPost("/{serverId}/files/{root}/upload", FileManagerEndpoints.UploadAsync).RequireAntiforgery().Audited(AuditCategories.Files, "upload");
        group.MapPost("/{serverId}/files/{root}/mkdir", FileManagerEndpoints.CreateDirectoryAsync).RequireAntiforgery().Audited(AuditCategories.Files, "mkdir");
        group.MapPost("/{serverId}/files/{root}/rename", FileManagerEndpoints.RenameAsync).RequireAntiforgery().Audited(AuditCategories.Files, "rename");
        group.MapPost("/{serverId}/files/{root}/move", FileManagerEndpoints.MoveAsync).RequireAntiforgery().Audited(AuditCategories.Files, "move");
        group.MapDelete("/{serverId}/files/{root}", FileManagerEndpoints.DeleteAsync).RequireAntiforgery().Audited(AuditCategories.Files, "delete");
        group.MapPost("/{serverId}/files/{root}/download-archive", FileManagerEndpoints.PrepareDownloadArchiveAsync).RequireAntiforgery().Audited(AuditCategories.Files, "download-archive");
        group.MapGet("/{serverId}/files/{root}/download-archive/{archiveId}", FileManagerEndpoints.DownloadArchiveAsync);
        group.MapGet("/{serverId}/files/{root}/trash", FileManagerEndpoints.ListTrashAsync);
        group.MapPost("/{serverId}/files/{root}/trash/{trashId}/restore", FileManagerEndpoints.RestoreTrashAsync).RequireAntiforgery().Audited(AuditCategories.Files, "trash-restore");
        group.MapDelete("/{serverId}/files/{root}/trash/{trashId}", FileManagerEndpoints.PurgeTrashEntryAsync).RequireAntiforgery().Audited(AuditCategories.Files, "trash-purge");
        group.MapDelete("/{serverId}/files/{root}/trash", FileManagerEndpoints.EmptyTrashAsync).RequireAntiforgery().Audited(AuditCategories.Files, "trash-empty");
        group.MapPost("/{serverId}/files/{root}/compress", FileManagerEndpoints.CompressAsync).RequireAntiforgery().Audited(AuditCategories.Files, "compress");
        group.MapPost("/{serverId}/files/{root}/extract", FileManagerEndpoints.ExtractAsync).RequireAntiforgery().Audited(AuditCategories.Files, "extract");
        group.MapGet("/operations", FileManagerEndpoints.GetRecentOperations);
        group.MapGet("/operations/{taskId}", FileManagerEndpoints.GetOperationStatusAsync);
        group.MapDelete("/operations/{taskId}", FileManagerEndpoints.CancelOperation).RequireAntiforgery().Audited(AuditCategories.Files, "cancel-operation");

        return endpoints;
    }
}
