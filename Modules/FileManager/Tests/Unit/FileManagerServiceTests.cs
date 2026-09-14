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
    [InlineData("C:\\absolute")]
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

        await service.SaveTextContentAsync("server-1", "data", "file.txt", "Saved Content", CancellationToken.None);

        Assert.Equal("data-files", repo.LastOperation);
        Assert.Equal("file.txt", repo.LastRelativePath);
        Assert.Equal("Saved Content"u8.ToArray(), repo.WrittenPayload);
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
    public async Task DeleteAsync_delegates_to_repository_when_writable()
    {
        var repo = new FakeFileRepository([]);
        var service = CreateService(repo);

        await service.DeleteAsync("server-1", "data", "sub/file.txt", CancellationToken.None);

        Assert.Equal("data-files", repo.LastOperation);
        Assert.Equal("sub/file.txt", repo.LastRelativePath);
    }

    [Fact]
    public async Task BackgroundOperationTracker_tracks_task_status_lifecycle()
    {
        var tracker = new BackgroundOperationTracker();
        var tcs = new TaskCompletionSource();

        var taskId = tracker.StartTracking("Test task", async ct => await tcs.Task);

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

        var taskId = tracker.StartTracking("Faulty task", ct => throw new InvalidOperationException("Failed task error."));
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

        var taskId = await service.ArchiveAsync("server-1", "data", "sub/folder", "backup.zip", CancellationToken.None);

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
            service.ArchiveAsync("server-1", "server", "sub/folder", "backup.zip", CancellationToken.None));
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

        public Task<IReadOnlyList<FileEntryDto>> ListAsync(
            string operation,
            string relativePath,
            CancellationToken cancellationToken)
        {
            LastOperation = operation;
            LastRelativePath = relativePath;
            return Task.FromResult(entries);
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

        public async Task WriteAsync(
            string operation,
            string relativePath,
            Stream source,
            CancellationToken cancellationToken)
        {
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

        public Task DeleteAsync(string operation, string relativePath, CancellationToken cancellationToken)
        {
            LastOperation = operation;
            LastRelativePath = relativePath;
            return Task.CompletedTask;
        }

        public Task ArchiveAsync(string operation, string relativePath, string zipPath, CancellationToken cancellationToken)
        {
            LastOperation = operation;
            LastRelativePath = relativePath;
            LastDestinationPath = zipPath;
            return Task.CompletedTask;
        }

        public Task ExtractAsync(string operation, string zipPath, string destPath, CancellationToken cancellationToken)
        {
            LastOperation = operation;
            LastRelativePath = zipPath;
            LastDestinationPath = destPath;
            return Task.CompletedTask;
        }
    }
}
