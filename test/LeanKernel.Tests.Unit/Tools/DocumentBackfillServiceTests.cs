using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Knowledge;
using LeanKernel.Tools;
using LeanKernel.Tools.Ingestion;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace LeanKernel.Tests.Unit.Tools;

public sealed class DocumentBackfillServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly DocumentLibraryService _libraryService;
    private readonly Mock<IDocumentFingerprintService> _fingerprintMock;

    public DocumentBackfillServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"lk-backfill-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);

        var config = new LeanKernelConfig
        {
            FileSystem = new FileSystemConfig
            {
                AllowedRoot = _tempRoot,
                MaxExtractedCharacters = 10_000
            },
            DocumentIngestion = new DocumentIngestionConfig
            {
                Enabled = true,
                MaxConcurrentJobs = 1,
                ManagedStoragePath = Path.Combine(_tempRoot, "managed-documents")
            }
        };

        _libraryService = new DocumentLibraryService(
            new RecordingKnowledgeService(),
            CreateGBrainClient(),
            Options.Create(config),
            NullLogger<DocumentLibraryService>.Instance);

        _fingerprintMock = new Mock<IDocumentFingerprintService>();
        _fingerprintMock.Setup(f => f.ComputeFingerprint(It.IsAny<string>()))
            .Returns((string p) => $"fp:{Path.GetFileName(p)}");
        _fingerprintMock.Setup(f => f.IsKnownFingerprintAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _fingerprintMock.Setup(f => f.RecordFingerprintAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Fact]
    public void Constructor_null_libraryService_throws()
    {
        var act = () => new DocumentBackfillService(
            null!,
            Mock.Of<IDocumentFingerprintService>(),
            NullLogger<DocumentBackfillService>.Instance);

        act.Should().Throw<ArgumentNullException>()
            .Which.ParamName.Should().Be("libraryService");
    }

    [Fact]
    public void Constructor_null_fingerprintService_throws()
    {
        var act = () => new DocumentBackfillService(
            _libraryService,
            null!,
            NullLogger<DocumentBackfillService>.Instance);

        act.Should().Throw<ArgumentNullException>()
            .Which.ParamName.Should().Be("fingerprintService");
    }

    [Fact]
    public void Constructor_null_logger_throws()
    {
        var act = () => new DocumentBackfillService(
            _libraryService,
            Mock.Of<IDocumentFingerprintService>(),
            null!);

        act.Should().Throw<ArgumentNullException>()
            .Which.ParamName.Should().Be("logger");
    }

    [Fact]
    public async Task RunBackfillAsync_emptySourceDirectory_throws()
    {
        var sut = CreateSut();

        var act = () => sut.RunBackfillAsync("", ct: CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task RunBackfillAsync_nonExistentDirectory_throwsDirectoryNotFoundException()
    {
        var sut = CreateSut();
        var fakePath = Path.Combine(_tempRoot, "no-such-dir");

        var act = () => sut.RunBackfillAsync(fakePath, ct: CancellationToken.None);

        await act.Should().ThrowAsync<DirectoryNotFoundException>();
    }

    [Fact]
    public async Task RunBackfillAsync_dryRun_withFiles_returnsCountAndDoesNotIngest()
    {
        var sourceDir = CreateSourceDirectory();
        CreateTestFile(sourceDir, "doc1.txt");
        CreateTestFile(sourceDir, "doc2.txt");
        var sut = CreateSut();

        var result = await sut.RunBackfillAsync(sourceDir, dryRun: true, ct: CancellationToken.None);

        result.Should().Be(2);
        _fingerprintMock.Verify(
            f => f.RecordFingerprintAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunBackfillAsync_duplicateFingerprint_skipsFile()
    {
        var sourceDir = CreateSourceDirectory();
        CreateTestFile(sourceDir, "dup.txt");
        _fingerprintMock.Setup(f => f.IsKnownFingerprintAsync("fp:dup.txt", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var sut = CreateSut();

        var result = await sut.RunBackfillAsync(sourceDir, dryRun: true, ct: CancellationToken.None);

        result.Should().Be(0);
        _fingerprintMock.Verify(
            f => f.RecordFingerprintAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunBackfillAsync_newFingerprint_ingestsFile()
    {
        var sourceDir = CreateSourceDirectory();
        CreateTestFile(sourceDir, "fresh.txt");
        var sut = CreateSut();

        await sut.RunBackfillAsync(sourceDir, ct: CancellationToken.None);

        _fingerprintMock.Verify(
            f => f.RecordFingerprintAsync(
                "fp:fresh.txt",
                It.IsAny<string>(),
                It.IsAny<long>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunBackfillAsync_withTags_passesTagsToIngestion()
    {
        var sourceDir = CreateSourceDirectory();
        CreateTestFile(sourceDir, "tagged.txt");
        var sut = CreateSut();
        var tags = new List<string> { "auto", "backfill" };

        await sut.RunBackfillAsync(sourceDir, tags: tags, ct: CancellationToken.None);

        _fingerprintMock.Verify(
            f => f.RecordFingerprintAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<long>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunBackfillAsync_withCheckpoint_writesAndClearsCheckpointOnSuccess()
    {
        var sourceDir = CreateSourceDirectory();
        CreateTestFile(sourceDir, "ckpt.txt");
        var checkpointPath = Path.Combine(_tempRoot, "checkpoint.txt");
        var sut = CreateSut();

        await sut.RunBackfillAsync(
            sourceDir, checkpointPath: checkpointPath, ct: CancellationToken.None);

        File.Exists(checkpointPath).Should().BeFalse("checkpoint is cleared after zero failures");
    }

    [Fact]
    public async Task RunBackfillAsync_fingerprintCheckThrows_stillProcessesFile()
    {
        var sourceDir = CreateSourceDirectory();
        CreateTestFile(sourceDir, "err.txt");
        _fingerprintMock.Setup(f => f.IsKnownFingerprintAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("fingerprint service down"));
        var sut = CreateSut();

        await sut.RunBackfillAsync(sourceDir, dryRun: true, ct: CancellationToken.None);

        _fingerprintMock.Verify(
            f => f.RecordFingerprintAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunBackfillAsync_resumeFromCheckpoint_skipsAlreadyProcessedFiles()
    {
        var sourceDir = CreateSourceDirectory();
        CreateTestFile(sourceDir, "a.txt");
        CreateTestFile(sourceDir, "b.txt");
        CreateTestFile(sourceDir, "c.txt");
        var checkpointPath = Path.Combine(_tempRoot, "checkpoint.txt");
        await File.WriteAllTextAsync(checkpointPath, Path.Combine(sourceDir, "b.txt"));
        var sut = CreateSut();

        await sut.RunBackfillAsync(
            sourceDir, checkpointPath: checkpointPath, ct: CancellationToken.None);

        _fingerprintMock.Verify(
            f => f.RecordFingerprintAsync(
                It.IsAny<string>(),
                It.Is<string>(p => p.Contains("c.txt")),
                It.IsAny<long>(),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "only c.txt should be processed after checkpoint at b.txt");

        _fingerprintMock.Verify(
            f => f.RecordFingerprintAsync(
                It.IsAny<string>(),
                It.Is<string>(p => p.Contains("a.txt") || p.Contains("b.txt")),
                It.IsAny<long>(),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "a.txt and b.txt should be skipped due to checkpoint");
    }

    [Fact]
    public void BackfillOptions_defaultValues_matchExpected()
    {
        var options = new BackfillOptions("/src");

        options.Filter.Should().Be("*.*");
        options.Recursive.Should().BeTrue();
        options.MaxConcurrency.Should().Be(2);
        options.Tags.Should().BeNull();
        options.CheckpointPath.Should().BeNull();
        options.DryRun.Should().BeFalse();
    }

    [Fact]
    public void BackfillOptions_customValues_areStoredCorrectly()
    {
        var options = new BackfillOptions(
            SourceDirectory: "/custom",
            Filter: "*.pdf",
            Recursive: false,
            Tags: ["tag1"],
            MaxConcurrency: 8,
            CheckpointPath: "/tmp/ckpt",
            DryRun: true);

        options.SourceDirectory.Should().Be("/custom");
        options.Filter.Should().Be("*.pdf");
        options.Recursive.Should().BeFalse();
        options.Tags.Should().BeEquivalentTo(["tag1"]);
        options.MaxConcurrency.Should().Be(8);
        options.CheckpointPath.Should().Be("/tmp/ckpt");
        options.DryRun.Should().BeTrue();
    }

    private DocumentBackfillService CreateSut()
        => new(_libraryService, _fingerprintMock.Object, NullLogger<DocumentBackfillService>.Instance);

    private string CreateSourceDirectory()
    {
        var dir = Path.Combine(_tempRoot, "documents", "backfill-source", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void CreateTestFile(string directory, string fileName)
        => File.WriteAllText(Path.Combine(directory, fileName), $"test content for {fileName}");

    private static GBrainMcpClient CreateGBrainClient()
        => new(
            new HttpClient(new FakeHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    jsonrpc = "2.0",
                    id = 1,
                    result = new { path = "files/doc.txt" }
                })
            }))
            {
                BaseAddress = new Uri("http://localhost")
            },
            NullLogger<GBrainMcpClient>.Instance);

    private sealed class RecordingKnowledgeService : IKnowledgeService
    {
        public Task<IReadOnlyList<RetrievalCandidate>> SearchAsync(string query, int maxResults = 10, CancellationToken ct = default)
            => Task.FromResult((IReadOnlyList<RetrievalCandidate>)[]);

        public Task<KnowledgePage?> GetPageAsync(string key, CancellationToken ct = default)
            => Task.FromResult<KnowledgePage?>(null);

        public Task PutPageAsync(string key, string content, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task DeletePageAsync(string key, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(handler(request, cancellationToken));
    }
}
