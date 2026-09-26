using System.IO;
using System.Net.Http;
using System.Text;
using AlegacyWebPanel.Core.Errors;
using AlegacyWebPanel.Modules.AutomationApi.Endpoints;
using AlegacyWebPanel.Modules.FileManager.Configuration;
using AlegacyWebPanel.Modules.FileManager.Contracts;
using AlegacyWebPanel.Modules.FileManager.Exceptions;
using AlegacyWebPanel.Modules.FileManager.Services;
using AlegacyWebPanel.Modules.RemoteOperations.Exceptions;
using AlegacyWebPanel.Modules.ServerManagement.Contracts;
using AlegacyWebPanel.Modules.ServerManagement.Exceptions;
using AlegacyWebPanel.Modules.ServerManagement.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AlegacyWebPanel.AutomationApi.FeatureTests;

public sealed class AutomationApiEndpointTests
{
    [Fact]
    public async Task ListServers_returns_configured_summaries()
    {
        var service = new FakeServerService();

        var result = await AutomationApiEndpoints.ListServersAsync(service, CancellationToken.None);

        var response = Assert.IsType<Ok<IReadOnlyList<ServerSummary>>>(result);
        Assert.Equal("main", Assert.Single(response.Value!).Id);
    }

    [Fact]
    public async Task Lifecycle_forwards_action_and_returns_service_response()
    {
        var service = new FakeServerService();

        var result = await AutomationApiEndpoints.LifecycleAsync(
            "main", ServerLifecycleAction.Restart, service, CancellationToken.None);

        var response = Assert.IsType<Ok<ServerLifecycleResponse>>(result);
        Assert.Equal(ServerLifecycleAction.Restart, service.LifecycleAction);
        Assert.Equal("main", response.Value!.ServerId);
        Assert.Equal(ServerRuntimeStatus.Offline, response.Value.Status);
    }

    [Theory]
    [InlineData(ServerFailure.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(ServerFailure.Conflict, StatusCodes.Status409Conflict)]
    [InlineData(ServerFailure.OperationFailed, StatusCodes.Status502BadGateway)]
    [InlineData(ServerFailure.Unavailable, StatusCodes.Status503ServiceUnavailable)]
    public async Task Lifecycle_translates_domain_failures(ServerFailure failure, int expectedStatus)
    {
        var exception = await Assert.ThrowsAsync<HttpException>(() =>
            AutomationApiEndpoints.LifecycleAsync(
                "main",
                ServerLifecycleAction.Stop,
                new FakeServerService { Failure = failure },
                CancellationToken.None));

        Assert.Equal(expectedStatus, exception.StatusCode);
    }

    [Fact]
    public async Task ListFiles_forwards_server_root_and_path()
    {
        var service = new FakeFileService();

        var result = await AutomationApiEndpoints.ListFilesAsync(
            "main", "data", service, CancellationToken.None, "backups");

        Assert.IsType<Ok<DirectoryListingDto>>(result);
        Assert.Equal("main", service.ServerId);
        Assert.Equal("data", service.RootId);
        Assert.Equal("backups", service.Path);
    }

    [Theory]
    [InlineData(FileFailure.InstanceNotFound, StatusCodes.Status404NotFound)]
    [InlineData(FileFailure.RootNotFound, StatusCodes.Status404NotFound)]
    [InlineData(FileFailure.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(FileFailure.InvalidPath, StatusCodes.Status400BadRequest)]
    [InlineData(FileFailure.PermissionDenied, StatusCodes.Status403Forbidden)]
    [InlineData(FileFailure.TooLarge, StatusCodes.Status413PayloadTooLarge)]
    [InlineData(FileFailure.Unsupported, StatusCodes.Status415UnsupportedMediaType)]
    [InlineData(FileFailure.RemoteFailed, StatusCodes.Status502BadGateway)]
    public async Task File_operations_translate_domain_failures(FileFailure failure, int expectedStatus)
    {
        var exception = await Assert.ThrowsAsync<HttpException>(() =>
            AutomationApiEndpoints.ListFilesAsync(
                "main",
                "data",
                new FakeFileService { Failure = failure },
                CancellationToken.None,
                "backups"));

        Assert.Equal(expectedStatus, exception.StatusCode);
    }

    [Fact]
    public async Task Upload_parses_multipart_file_and_forwards_relative_path()
    {
        var service = new FakeFileService();
        var context = await CreateUploadContextAsync("world.zip", "archived-world");

        var result = await AutomationApiEndpoints.UploadFileAsync(
            "main", "data", context.Request, service, CreateFileOptions(), CancellationToken.None, "backups");

        Assert.IsType<Ok>(result);
        Assert.Equal("main", service.ServerId);
        Assert.Equal("data", service.RootId);
        Assert.Equal("backups/world.zip", service.Path);
        Assert.Equal("archived-world", service.UploadedContent);
    }

    [Fact]
    public async Task Upload_defaults_target_path_to_the_root()
    {
        var service = new FakeFileService();
        var context = await CreateUploadContextAsync("world.zip", "archived-world");

        await AutomationApiEndpoints.UploadFileAsync(
            "main", "data", context.Request, service, CreateFileOptions(), CancellationToken.None);

        Assert.Equal("world.zip", service.Path);
    }

    [Fact]
    public async Task Upload_rejects_non_multipart_requests()
    {
        var context = new DefaultHttpContext();
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream([1, 2, 3]);

        var result = await AutomationApiEndpoints.UploadFileAsync(
            "main", "data", context.Request, new FakeFileService(), CreateFileOptions(), CancellationToken.None);

        var badRequest = Assert.IsType<BadRequest<string>>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequest.StatusCode);
        Assert.Equal("Request must be multipart/form-data", badRequest.Value);
    }

    [Fact]
    public async Task Upload_rejects_oversized_bodies_before_reading_them()
    {
        var context = new DefaultHttpContext();
        context.Request.ContentType = "multipart/form-data; boundary=test-boundary";
        context.Request.ContentLength = 10 * 1024 * 1024;
        context.Request.Body = new MemoryStream();

        var exception = await Assert.ThrowsAsync<HttpException>(() =>
            AutomationApiEndpoints.UploadFileAsync(
                "main",
                "data",
                context.Request,
                new FakeFileService(),
                Options.Create(new FileManagerOptions { MaximumFileSizeBytes = 1024 }),
                CancellationToken.None));

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, exception.StatusCode);
    }

    [Fact]
    public async Task Download_streams_file_content_to_the_response()
    {
        var service = new FakeFileService
        {
            DownloadContent = "payload",
            Entries = [new FileEntryDto("world.zip", false, 7, DateTimeOffset.UtcNow)]
        };

        var result = await AutomationApiEndpoints.DownloadFileAsync(
            "main", "data", service, CancellationToken.None, "backups/world.zip");

        var context = CreateHttpContext();
        await result.ExecuteAsync(context);

        var body = Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());
        Assert.Equal("payload", body);
        Assert.Equal("main", service.ServerId);
        Assert.Equal("data", service.RootId);
        Assert.Equal("backups/world.zip", service.Path);
    }

    [Fact]
    public async Task Download_translates_missing_file_to_not_found_before_streaming()
    {
        var exception = await Assert.ThrowsAsync<HttpException>(() =>
            AutomationApiEndpoints.DownloadFileAsync(
                "main", "data", new FakeFileService(), CancellationToken.None, "backups/missing.zip"));

        Assert.Equal(StatusCodes.Status404NotFound, exception.StatusCode);
    }

    [Fact]
    public async Task Download_requires_a_path()
    {
        var result = await AutomationApiEndpoints.DownloadFileAsync(
            "main", "data", new FakeFileService(), CancellationToken.None);

        var badRequest = Assert.IsType<BadRequest<string>>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequest.StatusCode);
    }

    private static IOptions<FileManagerOptions> CreateFileOptions() =>
        Options.Create(new FileManagerOptions());

    private static async Task<DefaultHttpContext> CreateUploadContextAsync(string fileName, string content)
    {
        using var multipart = new MultipartFormDataContent();
        multipart.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(content)), "file", fileName);

        var body = new MemoryStream();
        await multipart.CopyToAsync(body);
        body.Position = 0;

        var context = new DefaultHttpContext();
        context.Request.ContentType = multipart.Headers.ContentType!.ToString();
        context.Request.ContentLength = body.Length;
        context.Request.Body = body;
        return context;
    }

    private static DefaultHttpContext CreateHttpContext()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddProblemDetails();

        return new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
            Response = { Body = new MemoryStream() }
        };
    }

    public enum ServerFailure
    {
        None,
        NotFound,
        Conflict,
        OperationFailed,
        Unavailable
    }

    private sealed class FakeServerService : IServerManagementService
    {
        public ServerFailure Failure { get; init; }

        public ServerLifecycleAction? LifecycleAction { get; private set; }

        public Task<IReadOnlyList<ServerSummary>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ServerSummary>>(
                [new ServerSummary("main", "Main server", "localhost", 42420, "Local")]);

        public Task<ServerStatusResponse> GetStatusAsync(string serverId, CancellationToken cancellationToken)
        {
            ThrowIfRequested(serverId);
            return Task.FromResult(new ServerStatusResponse(serverId, ServerRuntimeStatus.Online));
        }

        public Task<ServerLifecycleResponse> ExecuteLifecycleAsync(
            string serverId,
            ServerLifecycleAction action,
            CancellationToken cancellationToken)
        {
            ThrowIfRequested(serverId);
            LifecycleAction = action;
            return Task.FromResult(new ServerLifecycleResponse(serverId, action, ServerRuntimeStatus.Offline));
        }

        public Task<SendServerCommandResponse> SendCommandAsync(
            string serverId,
            string command,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ServerMetricsResponse> GetMetricsAsync(
            string serverId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IAsyncEnumerable<ServerLogEvent>> OpenLogStreamAsync(
            string serverId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        private void ThrowIfRequested(string serverId)
        {
            if (Failure == ServerFailure.None)
            {
                return;
            }

            throw Failure switch
            {
                ServerFailure.NotFound => new ServerNotFoundException(serverId),
                ServerFailure.Conflict => new ServerOperationConflictException(serverId),
                ServerFailure.OperationFailed => new ServerOperationFailedException("operation"),
                ServerFailure.Unavailable => new ServerUnavailableException("unavailable"),
                _ => new ArgumentOutOfRangeException()
            };
        }
    }

    public enum FileFailure
    {
        None,
        InstanceNotFound,
        RootNotFound,
        InvalidPath,
        PermissionDenied,
        TooLarge,
        Unsupported,
        NotFound,
        RemoteFailed
    }

    private sealed class FakeFileService : IFileManagerService
    {
        public FileFailure Failure { get; init; }

        public string DownloadContent { get; init; } = string.Empty;

        public IReadOnlyList<FileEntryDto> Entries { get; init; } = [];

        public string? ServerId { get; private set; }

        public string? RootId { get; private set; }

        public string? Path { get; private set; }

        public string? UploadedContent { get; private set; }

        public Task<DirectoryListingDto> GetDirectoryListingAsync(
            string serverId,
            string rootId,
            string relativePath,
            CancellationToken cancellationToken)
        {
            ThrowIfRequested();
            Record(serverId, rootId, relativePath);
            return Task.FromResult(new DirectoryListingDto(relativePath, [], Entries));
        }

        public async Task DownloadFileAsync(
            string serverId,
            string rootId,
            string relativePath,
            Stream destination,
            CancellationToken cancellationToken)
        {
            ThrowIfRequested();
            Record(serverId, rootId, relativePath);
            await destination.WriteAsync(Encoding.UTF8.GetBytes(DownloadContent), cancellationToken);
        }

        public async Task UploadFileAsync(
            string serverId,
            string rootId,
            string relativePath,
            Stream source,
            CancellationToken cancellationToken)
        {
            ThrowIfRequested();
            Record(serverId, rootId, relativePath);
            using var reader = new StreamReader(source, Encoding.UTF8, leaveOpen: true);
            UploadedContent = await reader.ReadToEndAsync(cancellationToken);
        }

        public Task<FileContentDto> GetTextContentAsync(
            string serverId,
            string rootId,
            string relativePath,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task SaveTextContentAsync(
            string serverId,
            string rootId,
            string relativePath,
            string content,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task CreateDirectoryAsync(
            string serverId,
            string rootId,
            string relativePath,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task RenameAsync(
            string serverId,
            string rootId,
            string relativePath,
            string newName,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task MoveAsync(
            string serverId,
            string rootId,
            string sourcePath,
            string destinationPath,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task DeleteAsync(
            string serverId,
            string rootId,
            string relativePath,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<string> ArchiveAsync(
            string serverId,
            string rootId,
            string relativePath,
            string zipPath,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<string> ExtractAsync(
            string serverId,
            string rootId,
            string zipPath,
            string destPath,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public TrackedOperationDto? GetOperationStatus(string taskId) => throw new NotSupportedException();

        private void Record(string serverId, string rootId, string relativePath)
        {
            ServerId = serverId;
            RootId = rootId;
            Path = relativePath;
        }

        private void ThrowIfRequested()
        {
            if (Failure == FileFailure.None)
            {
                return;
            }

            throw Failure switch
            {
                FileFailure.InstanceNotFound => new InstanceNotFoundException("main"),
                FileFailure.RootNotFound => new RootNotFoundException("data"),
                FileFailure.InvalidPath => new InvalidRelativePathException("invalid"),
                FileFailure.PermissionDenied => new PermissionDeniedException("denied"),
                FileFailure.TooLarge => new FileTooLargeException("large"),
                FileFailure.Unsupported => new UnsupportedFileException("unsupported"),
                FileFailure.NotFound => new FileNotFoundException("missing"),
                FileFailure.RemoteFailed => new RemoteOperationFailedException("operation", 1, "error"),
                _ => new ArgumentOutOfRangeException()
            };
        }
    }
}
