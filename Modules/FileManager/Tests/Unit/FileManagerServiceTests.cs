using AlegacyWebPanel.Modules.FileManager.Configuration;
using AlegacyWebPanel.Modules.FileManager.Contracts;
using AlegacyWebPanel.Modules.FileManager.Exceptions;
using AlegacyWebPanel.Modules.FileManager.Persistence;
using AlegacyWebPanel.Modules.FileManager.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AlegacyWebPanel.FileManager.UnitTests;

public sealed class FileManagerServiceTests
{
    private static readonly FileManagerOptions TestOptions = new()
    {
        Instances = new Dictionary<string, ServerInstanceRoots>
        {
            {
                "server-1",
                new ServerInstanceRoots
                {
                    Roots = new Dictionary<string, FileRootConfig>
                    {
                        {
                            "server",
                            new FileRootConfig
                            {
                                DisplayName = "Server Root",
                                Path = "/server-dir",
                                Operation = "server-files",
                                IsWritable = false
                            }
                        },
                        {
                            "data",
                            new FileRootConfig
                            {
                                DisplayName = "Data Root",
                                Path = "/data-dir",
                                Operation = "data-files",
                                IsWritable = true
                            }
                        }
                    }
                }
            }
        }
    };

    private static FileManagerService CreateService(IFileRepository repo, FileManagerOptions? options = null)
    {
        return new FileManagerService(repo, new BackgroundOperationTracker(), Options.Create(options ?? TestOptions), NullLogger<FileManagerService>.Instance);
    }

    [Fact]
    public async Task GetDirectoryListingAsync_returns_listing_successfully()
    {
        var entries = new List<FileEntryDto>
        {
            new("file1.json", false, 123, DateTimeOffset.UtcNow),
            new("dir1", true, 0, DateTimeOffset.UtcNow)
        };
        var repo = new FakeFileRepository(entries);
        var service = CreateService(repo);

        var result = await service.GetDirectoryListingAsync("server-1", "server", "ModConfig", CancellationToken.None);

        Assert.Equal("ModConfig", result.CurrentPath);
        Assert.Equal(2, result.Roots.Count);
        Assert.Equal("server", result.Roots[0].Id);
        Assert.False(result.Roots[0].IsWritable);
        Assert.Equal("data", result.Roots[1].Id);
        Assert.True(result.Roots[1].IsWritable);
        
        Assert.Equal(2, result.Entries.Count);
        Assert.Equal("file1.json", result.Entries[0].Name);
        Assert.False(result.Entries[0].IsFolder);
        Assert.Equal(123, result.Entries[0].Size);
        Assert.Equal("dir1", result.Entries[1].Name);
        Assert.True(result.Entries[1].IsFolder);
        
        Assert.Equal("server-files", repo.LastOperation);
        Assert.Equal("ModConfig", repo.LastRelativePath);
    }

    [Fact]
    public async Task GetDirectoryListingAsync_throws_InstanceNotFoundException_when_server_missing()
    {
        var service = CreateService(new FakeFileRepository([]));

        await Assert.ThrowsAsync<InstanceNotFoundException>(() =>
            service.GetDirectoryListingAsync("missing-server", "server", "", CancellationToken.None));
    }

    [Fact]
    public async Task GetDirectoryListingAsync_throws_RootNotFoundException_when_root_missing()
    {
        var service = CreateService(new FakeFileRepository([]));

        await Assert.ThrowsAsync<RootNotFoundException>(() =>
            service.GetDirectoryListingAsync("server-1", "missing-root", "", CancellationToken.None));
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../escaped")]
    [InlineData("sub/../escaped")]
    [InlineData("sub\\..\\escaped")]
    [InlineData("/absolute")]
    [InlineData("~/relative")]
    [InlineData("control\nchar")]
    [InlineData("null\0char")]
    public async Task GetDirectoryListingAsync_throws_InvalidRelativePathException_on_invalid_paths(string path)
    {
        var service = CreateService(new FakeFileRepository([]));

        await Assert.ThrowsAsync<InvalidRelativePathException>(() =>
            service.GetDirectoryListingAsync("server-1", "server", path, CancellationToken.None));
    }

    [Fact]
    public async Task DownloadFileAsync_delegates_to_repository()
    {
        var repo = new FakeFileRepository([], [1, 2, 3]);
        var service = CreateService(repo);
        using var dest = new MemoryStream();

        await service.DownloadFileAsync("server-1", "server", "sub/file.bin", dest, CancellationToken.None);

        Assert.Equal("server-files", repo.LastOperation);
        Assert.Equal("sub/file.bin", repo.LastRelativePath);
        Assert.Equal([1, 2, 3], dest.ToArray());
    }

    [Fact]
    public async Task UploadFileAsync_delegates_to_repository_when_writable()
    {
        var repo = new FakeFileRepository([]);
        var service = CreateService(repo);
        using var source = new MemoryStream([4, 5, 6]);

        await service.UploadFileAsync("server-1", "data", "sub/upload.bin", source, CancellationToken.None);

        Assert.Equal("data-files", repo.LastOperation);
        Assert.Equal("sub/upload.bin", repo.LastRelativePath);
        Assert.Equal([4, 5, 6], repo.WrittenPayload);
    }

    [Fact]
    public async Task UploadFileAsync_throws_PermissionDeniedException_when_readonly()
    {
        var repo = new FakeFileRepository([]);
        var service = CreateService(repo);
        using var source = new MemoryStream([4, 5, 6]);

        await Assert.ThrowsAsync<PermissionDeniedException>(() =>
            service.UploadFileAsync("server-1", "server", "sub/upload.bin", source, CancellationToken.None));
    }

    [Fact]
    public async Task UploadFileAsync_throws_FileTooLargeException_when_limit_exceeded()
    {
        var options = new FileManagerOptions
        {
            MaximumFileSizeBytes = 5,
            Instances = TestOptions.Instances
        };
        var repo = new FakeFileRepository([]);
        var service = CreateService(repo, options);
        using var source = new MemoryStream([1, 2, 3, 4, 5, 6]);

        await Assert.ThrowsAsync<FileTooLargeException>(() =>
            service.UploadFileAsync("server-1", "data", "sub/upload.bin", source, CancellationToken.None));
    }

    [Fact]
    public async Task GetTextContentAsync_returns_content_successfully()
    {
        var listing = new List<FileEntryDto>
        {
            new("file.txt", false, 12, DateTimeOffset.UtcNow)
        };
        var payload = "Hello World!"u8.ToArray();
        var repo = new FakeFileRepository(listing, payload);
        var service = CreateService(repo);

        var result = await service.GetTextContentAsync("server-1", "server", "file.txt", CancellationToken.None);

        Assert.Equal("Hello World!", result.Content);
    }

    [Fact]
    public async Task GetTextContentAsync_throws_FileTooLargeException_when_file_too_large()
    {
        var listing = new List<FileEntryDto>
        {
            new("file.txt", false, 9999999, DateTimeOffset.UtcNow)
        };
        var repo = new FakeFileRepository(listing);
        var service = CreateService(repo);

        await Assert.ThrowsAsync<FileTooLargeException>(() =>
            service.GetTextContentAsync("server-1", "server", "file.txt", CancellationToken.None));
    }

    [Fact]
    public async Task GetTextContentAsync_throws_UnsupportedFileException_when_binary_file()
    {
        var listing = new List<FileEntryDto>
        {
            new("file.bin", false, 4, DateTimeOffset.UtcNow)
        };
        var payload = new byte[] { 1, 2, 0, 4 };
        var repo = new FakeFileRepository(listing, payload);
        var service = CreateService(repo);

        await Assert.ThrowsAsync<UnsupportedFileException>(() =>
            service.GetTextContentAsync("server-1", "server", "file.bin", CancellationToken.None));
    }

    [Fact]
    public async Task SaveTextContentAsync_writes_successfully()
    {
        var repo = new FakeFileRepository([]);
        var service = CreateService(repo);

        await service.SaveTextContentAsync("server-1", "data", "file.txt", "Saved Content", null, CancellationToken.None);

        Assert.Equal("data-files", repo.LastOperation);
        Assert.Equal("file.txt", repo.LastRelativePath);
        Assert.Equal("Saved Content"u8.ToArray(), repo.WrittenPayload);
    }

    [Fact]
    public async Task GetTextContentAsync_preserves_a_utf8_bom_on_round_trip()
    {
        var payload = new byte[] { 0xEF, 0xBB, 0xBF, (byte)'{', (byte)'}' };
        var repo = new FakeFileRepository([new("file.json", false, payload.Length, DateTimeOffset.UtcNow)], payload);
        var service = CreateService(repo);

        var content = await service.GetTextContentAsync("server-1", "data", "file.json", CancellationToken.None);
        await service.SaveTextContentAsync("server-1", "data", "file.json", content.Content, null, CancellationToken.None);

        Assert.Equal(payload, repo.WrittenPayload);
    }

    [Fact]
    public async Task GetTextContentAsync_rejects_invalid_utf8_instead_of_corrupting_it()
    {
        var payload = new byte[] { (byte)'a', 0xE9, (byte)'b' }; // Latin-1 "é"
        var repo = new FakeFileRepository([new("file.cfg", false, payload.Length, DateTimeOffset.UtcNow)], payload);
        var service = CreateService(repo);

        await Assert.ThrowsAsync<UnsupportedFileException>(() =>
            service.GetTextContentAsync("server-1", "data", "file.cfg", CancellationToken.None));
    }

    [Fact]
    public async Task GetTextContentAsync_matches_file_names_case_sensitively()
    {
        var repo = new FakeFileRepository([new("Config.json", false, 2, DateTimeOffset.UtcNow)], "{}"u8.ToArray());
        var service = CreateService(repo);

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            service.GetTextContentAsync("server-1", "data", "config.json", CancellationToken.None));
    }

    [Fact]
    public async Task SaveTextContentAsync_refuses_when_file_changed_since_it_was_opened()
    {
        var opened = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        var repo = new FakeFileRepository([new("file.txt", false, 1, opened)]) { StatModified = opened.AddMinutes(1) };
        var service = CreateService(repo);

        await Assert.ThrowsAsync<FileChangedException>(() =>
            service.SaveTextContentAsync("server-1", "data", "file.txt", "x", opened, CancellationToken.None));
        Assert.Null(repo.WrittenPayload);
    }

    [Fact]
    public async Task SaveTextContentAsync_writes_when_file_is_unchanged()
    {
        var opened = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        var repo = new FakeFileRepository([new("file.txt", false, 1, opened)]);
        var service = CreateService(repo);

        await service.SaveTextContentAsync("server-1", "data", "file.txt", "x", opened, CancellationToken.None);

        Assert.Equal("x"u8.ToArray(), repo.WrittenPayload);
    }

    [Fact]
    public async Task GetFileInfoAsync_rejects_folders_and_missing_files()
    {
        var repo = new FakeFileRepository([new("Saves", true, 0, DateTimeOffset.UtcNow)]);
        var service = CreateService(repo);

        await Assert.ThrowsAsync<InvalidFileOperationException>(() =>
            service.GetFileInfoAsync("server-1", "data", "Saves", CancellationToken.None));
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            service.GetFileInfoAsync("server-1", "data", "missing.txt", CancellationToken.None));
    }

    [Fact]
    public async Task GetDirectoryListingAsync_applies_the_listing_limit()
    {
        var entries = Enumerable.Range(0, 5).Select(i => new FileEntryDto($"f{i}", false, 1, DateTimeOffset.UtcNow)).ToList();
        var repo = new FakeFileRepository(entries);
        var options = TestOptionsWith(o => o.MaximumListingEntries = 3);
        var service = CreateService(repo, options);

        var listing = await service.GetDirectoryListingAsync("server-1", "data", "", CancellationToken.None);

        Assert.Equal(3, repo.LastMaximumListingEntries);
        Assert.Equal(3, listing.Entries.Count);
        Assert.True(listing.Truncated);
    }

    [Fact]
    public async Task GetDirectoryListingAsync_allows_colons_in_file_names()
    {
        var service = CreateService(new FakeFileRepository([]));

        await service.GetDirectoryListingAsync("server-1", "data", "Logs/crash-2026-09-01T12:00", CancellationToken.None);
    }

    [Fact]
    public async Task ExtractAsync_passes_the_extraction_limits()
    {
        var repo = new FakeFileRepository([]);
        var service = CreateService(repo);

        var taskId = await service.ExtractAsync("server-1", "data", "backup.zip", "", CancellationToken.None);
        await WaitForCompletionAsync(service, taskId);

        Assert.Equal(TestOptions.MaximumArchiveSizeBytes, repo.LastMaximumExtractedBytes);
    }

    [Fact]
    public async Task ArchiveAsync_passes_each_source_path_separately()
    {
        var repo = new FakeFileRepository([]);
        var service = CreateService(repo);

        var taskId = await service.ArchiveAsync("server-1", "data", ["a;b.txt", "dir"], "out.zip", CancellationToken.None);
        await WaitForCompletionAsync(service, taskId);

        Assert.Equal(["a;b.txt", "dir"], repo.LastSourcePaths);
    }

    [Fact]
    public void BackgroundOperationTracker_rejects_operations_over_the_concurrency_limit()
    {
        var tracker = new BackgroundOperationTracker(Options.Create(new FileManagerOptions { MaximumConcurrentOperations = 1 }));
        var gate = new TaskCompletionSource();

        tracker.StartTracking("first", null, (_, _) => gate.Task);

        Assert.Throws<TooManyOperationsException>(() => tracker.StartTracking("second", null, (_, _) => Task.CompletedTask));
        gate.SetResult();
    }

    [Fact]
    public async Task ArchiveAsync_reports_progress_and_target_folder()
    {
        var gate = new TaskCompletionSource();
        var repo = new FakeFileRepository([])
        {
            OnArchive = async (progress, _) =>
            {
                progress?.Report(new OperationProgress(2, 5, 200, 500));
                await gate.Task;
            }
        };
        var service = CreateService(repo);

        var taskId = await service.ArchiveAsync("server-1", "data", ["Mods"], "Backups/mods.zip", CancellationToken.None);
        await Task.Delay(50);
        var running = service.GetOperationStatus(taskId);
        gate.SetResult();
        await WaitForCompletionAsync(service, taskId);

        Assert.Equal(new OperationProgress(2, 5, 200, 500), running?.Progress);
        Assert.Equal(new OperationTarget("server-1", "data", "Backups"), running?.Target);
        Assert.Contains(service.GetRecentOperations(), op => op.TaskId == taskId);
    }

    [Fact]
    public async Task CancelOperation_marks_a_running_operation_cancelled()
    {
        var repo = new FakeFileRepository([])
        {
            OnArchive = (_, ct) => Task.Delay(Timeout.Infinite, ct)
        };
        var service = CreateService(repo);

        var taskId = await service.ArchiveAsync("server-1", "data", ["Mods"], "mods.zip", CancellationToken.None);
        Assert.True(service.CancelOperation(taskId));
        await WaitForCompletionAsync(service, taskId);

        Assert.Equal("Cancelled", service.GetOperationStatus(taskId)?.Status);
        Assert.False(service.CancelOperation(taskId));
    }

    [Fact]
    public async Task SearchAsync_passes_the_recursive_flag_and_returns_matches()
    {
        var repo = new FakeFileRepository([new("serverconfig.json", false, 10, DateTimeOffset.UtcNow), new("Mods", true, 0, DateTimeOffset.UtcNow)]);
        var service = CreateService(repo);

        var response = await service.SearchAsync("server-1", "data", "", "  CONFIG ", recursive: true, CancellationToken.None);

        Assert.True(repo.LastRecursive);
        Assert.Equal("serverconfig.json", Assert.Single(response.Results).Path);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a\nb")]
    public async Task SearchAsync_rejects_empty_or_control_text(string query)
    {
        var service = CreateService(new FakeFileRepository([]));

        await Assert.ThrowsAsync<InvalidFileOperationException>(() =>
            service.SearchAsync("server-1", "data", "", query, recursive: false, CancellationToken.None));
    }

    [Fact]
    public async Task UploadFileAsync_passes_createFolders_for_folder_uploads()
    {
        var repo = new FakeFileRepository([]);
        var service = CreateService(repo);

        await service.UploadFileAsync("server-1", "data", "Mods/pack/a.json", new MemoryStream([1]), CancellationToken.None, createFolders: true);

        Assert.True(repo.LastCreateParents);
    }

    [Fact]
    public async Task Download_archive_is_built_then_streamed_and_deleted()
    {
        var repo = new FakeFileRepository([], [7, 7]);
        var service = CreateService(repo);

        var prepared = await service.PrepareDownloadArchiveAsync("server-1", "data", ["Mods", "serverconfig.json"], CancellationToken.None);
        await WaitForCompletionAsync(service, prepared.TaskId);
        using var output = new MemoryStream();
        await service.DownloadArchiveAsync("server-1", "data", prepared.ArchiveId, output, CancellationToken.None);

        Assert.Equal(prepared.ArchiveId, repo.LastArchiveId);
        Assert.Equal($".panel-tmp/{prepared.ArchiveId}.zip", repo.LastRelativePath);
        Assert.Equal(prepared.ArchiveId, repo.DeletedArchiveId);
        Assert.Equal(new byte[] { 7, 7 }, output.ToArray());
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("ABC")]
    public async Task DownloadArchiveAsync_rejects_invalid_ids(string archiveId)
    {
        var service = CreateService(new FakeFileRepository([]));

        await Assert.ThrowsAsync<InvalidRelativePathException>(() =>
            service.DownloadArchiveAsync("server-1", "data", archiveId, Stream.Null, CancellationToken.None));
    }

    [Fact]
    public async Task Internal_tmp_folder_is_not_reachable_through_paths()
    {
        var service = CreateService(new FakeFileRepository([]));

        await Assert.ThrowsAsync<InvalidRelativePathException>(() =>
            service.GetDirectoryListingAsync("server-1", "data", ".panel-tmp", CancellationToken.None));
    }

    private static async Task WaitForCompletionAsync(FileManagerService service, string taskId)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (service.GetOperationStatus(taskId)?.Status is "Completed" or "Failed" or "Cancelled")
            {
                return;
            }

            await Task.Delay(10);
        }
    }

    private static FileManagerOptions TestOptionsWith(Action<FileManagerOptions> configure)
    {
        var options = new FileManagerOptions { Instances = TestOptions.Instances };
        configure(options);
        return options;
    }

    [Fact]
    public async Task CreateDirectoryAsync_delegates_to_repository_when_writable()
    {
        var repo = new FakeFileRepository([]);
        var service = CreateService(repo);

        await service.CreateDirectoryAsync("server-1", "data", "sub/new-dir", CancellationToken.None);

        Assert.Equal("data-files", repo.LastOperation);
        Assert.Equal("sub/new-dir", repo.LastRelativePath);
    }

    [Fact]
    public async Task CreateDirectoryAsync_throws_PermissionDeniedException_when_readonly()
    {
        var repo = new FakeFileRepository([]);
        var service = CreateService(repo);

        await Assert.ThrowsAsync<PermissionDeniedException>(() =>
            service.CreateDirectoryAsync("server-1", "server", "sub/new-dir", CancellationToken.None));
    }

    [Fact]
    public async Task RenameAsync_delegates_to_repository_when_writable_and_name_valid()
    {
        var repo = new FakeFileRepository([]);
        var service = CreateService(repo);

        await service.RenameAsync("server-1", "data", "sub/file.txt", "renamed.txt", CancellationToken.None);

        Assert.Equal("data-files", repo.LastOperation);
        Assert.Equal("sub/file.txt", repo.LastRelativePath);
        Assert.Equal("renamed.txt", repo.LastNewName);
    }

    [Theory]
    [InlineData("dir/file.txt")]
    [InlineData("dir\\file.txt")]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("control\nchar")]
    public async Task RenameAsync_throws_InvalidRelativePathException_when_name_contains_separators_or_invalid(string newName)
    {
        var repo = new FakeFileRepository([]);
        var service = CreateService(repo);

        await Assert.ThrowsAsync<InvalidRelativePathException>(() =>
            service.RenameAsync("server-1", "data", "sub/file.txt", newName, CancellationToken.None));
    }

    [Fact]
    public async Task MoveAsync_delegates_to_repository_when_writable()
    {
        var repo = new FakeFileRepository([]);
        var service = CreateService(repo);

        await service.MoveAsync("server-1", "data", "sub/file.txt", "other/file.txt", CancellationToken.None);

        Assert.Equal("data-files", repo.LastOperation);
        Assert.Equal("sub/file.txt", repo.LastRelativePath);
        Assert.Equal("other/file.txt", repo.LastDestinationPath);
    }

    [Fact]
    public async Task DeleteAsync_moves_to_trash_by_default()
    {
        var repo = new FakeFileRepository([]);
        var service = CreateService(repo);

        var trashed = await service.DeleteAsync("server-1", "data", "Saves/world.vcdbs", permanent: false, CancellationToken.None);

        Assert.Equal("Saves/world.vcdbs", repo.LastTrashedPath);
        Assert.Null(repo.LastDeletedPath);
        Assert.Equal(new FileManagerOptions().TrashRetentionDays, repo.LastRetentionDays);
        Assert.Equal("Saves/world.vcdbs", trashed?.OriginalPath);
    }

    [Fact]
    public async Task DeleteAsync_deletes_permanently_only_when_asked()
    {
        var repo = new FakeFileRepository([]);
        var service = CreateService(repo);

        var trashed = await service.DeleteAsync("server-1", "data", "sub/file.txt", permanent: true, CancellationToken.None);

        Assert.Null(trashed);
        Assert.Equal("sub/file.txt", repo.LastDeletedPath);
        Assert.Null(repo.LastTrashedPath);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/")]
    public async Task DeleteAsync_refuses_the_root_itself(string path)
    {
        var service = CreateService(new FakeFileRepository([]));

        await Assert.ThrowsAsync<InvalidRelativePathException>(() =>
            service.DeleteAsync("server-1", "data", path, permanent: false, CancellationToken.None));
    }

    [Theory]
    [InlineData(".trash")]
    [InlineData(".trash/123-4/item")]
    public async Task Paths_inside_the_trash_are_rejected(string path)
    {
        var service = CreateService(new FakeFileRepository([]));

        await Assert.ThrowsAsync<InvalidRelativePathException>(() =>
            service.DeleteAsync("server-1", "data", path, permanent: true, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidRelativePathException>(() =>
            service.MoveAsync("server-1", "data", "Saves", path, CancellationToken.None));
    }

    [Theory]
    [InlineData("../etc")]
    [InlineData("abc")]
    [InlineData("")]
    public async Task RestoreTrashAsync_rejects_invalid_ids(string id)
    {
        var service = CreateService(new FakeFileRepository([]));

        await Assert.ThrowsAsync<InvalidRelativePathException>(() =>
            service.RestoreTrashAsync("server-1", "data", id, CancellationToken.None));
    }

    [Fact]
    public async Task Trash_operations_require_a_writable_root()
    {
        var service = CreateService(new FakeFileRepository([]));

        await Assert.ThrowsAsync<PermissionDeniedException>(() =>
            service.ListTrashAsync("server-1", "server", CancellationToken.None));
        await Assert.ThrowsAsync<PermissionDeniedException>(() =>
            service.DeleteAsync("server-1", "server", "file.txt", permanent: false, CancellationToken.None));
    }

    [Fact]
    public async Task BackgroundOperationTracker_tracks_task_status_lifecycle()
    {
        var tracker = new BackgroundOperationTracker();
        var tcs = new TaskCompletionSource();

        var taskId = tracker.StartTracking("Test task", null, async (ct, _) => await tcs.Task);

        var statusRunning = tracker.GetStatus(taskId);
        Assert.NotNull(statusRunning);
        Assert.Equal("Test task", statusRunning.Description);
        Assert.Equal("Running", statusRunning.Status);
        Assert.Null(statusRunning.ErrorMessage);
        Assert.Null(statusRunning.Completed);

        tcs.SetResult();
        await Task.Delay(50); // allow async continuation to execute

        var statusCompleted = tracker.GetStatus(taskId);
        Assert.NotNull(statusCompleted);
        Assert.Equal("Completed", statusCompleted.Status);
        Assert.NotNull(statusCompleted.Completed);
    }

    [Fact]
    public async Task BackgroundOperationTracker_handles_exceptions_gracefully()
    {
        var tracker = new BackgroundOperationTracker();

        var taskId = tracker.StartTracking("Faulty task", null, (ct, _) => throw new InvalidOperationException("Failed task error."));
        await Task.Delay(50); // allow exception to bubble

        var statusFailed = tracker.GetStatus(taskId);
        Assert.NotNull(statusFailed);
        Assert.Equal("Failed", statusFailed.Status);
        Assert.Equal("Failed task error.", statusFailed.ErrorMessage);
        Assert.NotNull(statusFailed.Completed);
    }

    [Fact]
    public async Task ArchiveAsync_starts_background_operation_successfully()
    {
        var repo = new FakeFileRepository([]);
        var tracker = new BackgroundOperationTracker();
        var service = new FileManagerService(repo, tracker, Options.Create(TestOptions), NullLogger<FileManagerService>.Instance);

        var taskId = await service.ArchiveAsync("server-1", "data", ["sub/folder"], "backup.zip", CancellationToken.None);

        Assert.NotNull(taskId);
        var status = service.GetOperationStatus(taskId);
        Assert.NotNull(status);
        Assert.Equal("Compressing 'sub/folder' to 'backup.zip' in root 'data'", status.Description);
    }

    [Fact]
    public async Task ExtractAsync_starts_background_operation_successfully()
    {
        var repo = new FakeFileRepository([]);
        var tracker = new BackgroundOperationTracker();
        var service = new FileManagerService(repo, tracker, Options.Create(TestOptions), NullLogger<FileManagerService>.Instance);

        var taskId = await service.ExtractAsync("server-1", "data", "backup.zip", "extracted", CancellationToken.None);

        Assert.NotNull(taskId);
        var status = service.GetOperationStatus(taskId);
        Assert.NotNull(status);
        Assert.Equal("Extracting 'backup.zip' to 'extracted' in root 'data'", status.Description);
    }

    [Fact]
    public async Task ArchiveAsync_throws_PermissionDeniedException_when_readonly()
    {
        var repo = new FakeFileRepository([]);
        var tracker = new BackgroundOperationTracker();
        var service = new FileManagerService(repo, tracker, Options.Create(TestOptions), NullLogger<FileManagerService>.Instance);

        await Assert.ThrowsAsync<PermissionDeniedException>(() =>
            service.ArchiveAsync("server-1", "server", ["sub/folder"], "backup.zip", CancellationToken.None));
    }

    private sealed class FakeFileRepository(
        IReadOnlyList<FileEntryDto> entries,
        byte[]? readPayload = null) : IFileRepository
    {
        public string? LastOperation { get; private set; }
        public string? LastRelativePath { get; private set; }
        public byte[]? WrittenPayload { get; private set; }
        public string? LastNewName { get; private set; }
        public string? LastDestinationPath { get; private set; }

        public IReadOnlyList<string>? LastSourcePaths { get; private set; }
        public long? LastMaximumExtractedBytes { get; private set; }
        public int? LastMaximumListingEntries { get; private set; }
        public DateTimeOffset? StatModified { get; init; }

        public Task<DirectoryEntries> ListAsync(
            string operation,
            string relativePath,
            int maximumEntries,
            CancellationToken cancellationToken)
        {
            LastOperation = operation;
            LastRelativePath = relativePath;
            LastMaximumListingEntries = maximumEntries;
            return Task.FromResult(new DirectoryEntries(entries.Take(maximumEntries).ToList(), entries.Count > maximumEntries, 0));
        }

        public bool? LastRecursive { get; private set; }

        public Task<SearchResponseDto> SearchAsync(
            string operation,
            string relativePath,
            string query,
            bool recursive,
            int maximumResults,
            CancellationToken cancellationToken)
        {
            LastOperation = operation;
            LastRelativePath = relativePath;
            LastRecursive = recursive;
            var results = entries
                .Where(e => e.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Select(e => new SearchResultDto(relativePath.Length == 0 ? e.Name : $"{relativePath}/{e.Name}", e.Name, e.IsFolder, e.Size, e.Modified))
                .ToList();
            return Task.FromResult(new SearchResponseDto(results, false));
        }

        public Task<FolderSizeDto> MeasureAsync(string operation, string relativePath, CancellationToken cancellationToken)
        {
            LastRelativePath = relativePath;
            return Task.FromResult(new FolderSizeDto(123, 4, 1, true));
        }

        public Task<FileEntryDto?> StatAsync(
            string operation,
            string relativePath,
            CancellationToken cancellationToken)
        {
            var name = relativePath[(relativePath.LastIndexOf('/') + 1)..];
            var entry = entries.FirstOrDefault(e => e.Name == name);
            if (entry is not null && StatModified is { } modified)
            {
                entry = entry with { Modified = modified };
            }

            return Task.FromResult(entry);
        }

        public async Task ReadAsync(
            string operation,
            string relativePath,
            Stream destination,
            CancellationToken cancellationToken)
        {
            LastOperation = operation;
            LastRelativePath = relativePath;
            if (readPayload != null)
            {
                await destination.WriteAsync(readPayload, cancellationToken);
            }
        }

        public bool LastCreateParents { get; private set; }
        public string? LastArchiveId { get; private set; }
        public string? DeletedArchiveId { get; private set; }

        public Task ArchiveForDownloadAsync(string operation, string archiveId, IReadOnlyList<string> sourcePaths, IProgress<OperationProgress>? progress, CancellationToken cancellationToken)
        {
            LastArchiveId = archiveId;
            LastSourcePaths = sourcePaths;
            return Task.CompletedTask;
        }

        public Task DeleteDownloadArchiveAsync(string operation, string archiveId, CancellationToken cancellationToken)
        {
            DeletedArchiveId = archiveId;
            return Task.CompletedTask;
        }

        public async Task WriteAsync(
            string operation,
            string relativePath,
            Stream source,
            CancellationToken cancellationToken,
            bool createParents = false)
        {
            LastCreateParents = createParents;
            LastOperation = operation;
            LastRelativePath = relativePath;
            using var ms = new MemoryStream();
            await source.CopyToAsync(ms, cancellationToken);
            WrittenPayload = ms.ToArray();
        }

        public Task CreateDirectoryAsync(string operation, string relativePath, CancellationToken cancellationToken)
        {
            LastOperation = operation;
            LastRelativePath = relativePath;
            return Task.CompletedTask;
        }

        public Task RenameAsync(string operation, string relativePath, string newName, CancellationToken cancellationToken)
        {
            LastOperation = operation;
            LastRelativePath = relativePath;
            LastNewName = newName;
            return Task.CompletedTask;
        }

        public Task MoveAsync(string operation, string sourcePath, string destinationPath, CancellationToken cancellationToken)
        {
            LastOperation = operation;
            LastRelativePath = sourcePath;
            LastDestinationPath = destinationPath;
            return Task.CompletedTask;
        }

        public string? LastTrashedPath { get; private set; }
        public string? LastDeletedPath { get; private set; }
        public int? LastRetentionDays { get; private set; }

        public Task<TrashEntryDto> TrashAsync(string operation, string relativePath, int retentionDays, CancellationToken cancellationToken)
        {
            LastOperation = operation;
            LastTrashedPath = relativePath;
            LastRetentionDays = retentionDays;
            return Task.FromResult(new TrashEntryDto("1-1", relativePath, relativePath.Split('/')[^1], false, 1, DateTimeOffset.UtcNow));
        }

        public Task<IReadOnlyList<TrashEntryDto>> ListTrashAsync(string operation, int retentionDays, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TrashEntryDto>>([]);

        public Task<TrashEntryDto> RestoreTrashAsync(string operation, string trashId, CancellationToken cancellationToken) =>
            Task.FromResult(new TrashEntryDto(trashId, "restored", "restored", false, 1, DateTimeOffset.UtcNow));

        public Task PurgeTrashAsync(string operation, string? trashId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task DeleteAsync(string operation, string relativePath, CancellationToken cancellationToken)
        {
            LastDeletedPath = relativePath;
            LastOperation = operation;
            LastRelativePath = relativePath;
            return Task.CompletedTask;
        }

        public Func<IProgress<OperationProgress>?, CancellationToken, Task>? OnArchive { get; init; }

        public Task ArchiveAsync(string operation, IReadOnlyList<string> sourcePaths, string zipPath, IProgress<OperationProgress>? progress, CancellationToken cancellationToken)
        {
            LastOperation = operation;
            LastSourcePaths = sourcePaths;
            LastDestinationPath = zipPath;
            return OnArchive?.Invoke(progress, cancellationToken) ?? Task.CompletedTask;
        }

        public Task ExtractAsync(string operation, string zipPath, string destPath, long maximumExtractedBytes, int maximumEntries, IProgress<OperationProgress>? progress, CancellationToken cancellationToken)
        {
            LastMaximumExtractedBytes = maximumExtractedBytes;
            LastOperation = operation;
            LastRelativePath = zipPath;
            LastDestinationPath = destPath;
            return Task.CompletedTask;
        }
    }
}
