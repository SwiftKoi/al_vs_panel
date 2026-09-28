namespace AlegacyWebPanel.Modules.FileManager.Contracts;

public sealed record FileRootDto(
    string Id,
    string DisplayName,
    bool IsWritable,
    IReadOnlyList<string>? ProtectedPaths = null);

public sealed record TrashEntryDto(
    string Id,
    string OriginalPath,
    string Name,
    bool IsFolder,
    long Size,
    DateTimeOffset DeletedAt);

public sealed record FileEntryDto(
    string Name,
    bool IsFolder,
    long Size,
    DateTimeOffset Modified);

public sealed record DirectoryListingDto(
    string CurrentPath,
    IReadOnlyList<FileRootDto> Roots,
    IReadOnlyList<FileEntryDto> Entries,
    bool Truncated = false,
    int Skipped = 0,
    long MaximumUploadBytes = 0,
    int TrashRetentionDays = 0);

public sealed record SearchResultDto(
    string Path,
    string Name,
    bool IsFolder,
    long Size,
    DateTimeOffset Modified);

public sealed record SearchResponseDto(
    IReadOnlyList<SearchResultDto> Results,
    bool Truncated);

public sealed record FileContentDto(
    string Content,
    DateTimeOffset Modified);

public sealed record OperationProgress(
    long Items,
    long TotalItems,
    long Bytes,
    long TotalBytes);

/// <summary>Where an operation's result lands, so the UI can offer "open folder".</summary>
public sealed record OperationTarget(
    string ServerId,
    string RootId,
    string Folder);

public sealed record FolderSizeDto(
    long Bytes,
    long Files,
    long Folders,
    bool Complete);

public sealed record DownloadArchiveDto(
    string TaskId,
    string ArchiveId);

public sealed record TrackedOperationDto(
    string TaskId,
    string Description,
    string Status,
    string? ErrorMessage,
    DateTimeOffset Created,
    DateTimeOffset? Completed,
    OperationProgress? Progress = null,
    OperationTarget? Target = null);
