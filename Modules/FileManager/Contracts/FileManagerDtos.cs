namespace AlegacyWebPanel.Modules.FileManager.Contracts;

public sealed record FileRootDto(
    string Id,
    string DisplayName,
    bool IsWritable);

public sealed record FileEntryDto(
    string Name,
    bool IsFolder,
    long Size,
    DateTimeOffset Modified);

public sealed record DirectoryListingDto(
    string CurrentPath,
    IReadOnlyList<FileRootDto> Roots,
    IReadOnlyList<FileEntryDto> Entries);

public sealed record FileContentDto(
    string Content);

public sealed record TrackedOperationDto(
    string TaskId,
    string Description,
    string Status,
    string? ErrorMessage,
    DateTimeOffset Created,
    DateTimeOffset? Completed);
