using System.Text;
using Documents.Contracts;
using Documents.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Documents.Infrastructure.Tests;

/// <summary>G7-05 verify: save/read/delete round-trip, the traversal cases, the size cap, and that
/// DeleteAsync on a missing file does not throw. Each test gets its own throwaway root directory under
/// the OS temp folder, deleted in Dispose - real file I/O, no fakes.</summary>
public sealed class LocalFileStorageServiceTests : IDisposable
{
    private readonly string _root;

    public LocalFileStorageServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "dms-modular-tests", Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private IFileStorageService CreateService(long? maxUploadBytes = null, string[]? allowedExtensions = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["FileStorage:RootPath"] = _root,
        };
        if (maxUploadBytes is { } max)
            settings["FileStorage:MaxUploadBytes"] = max.ToString();

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings);
        if (allowedExtensions is not null)
        {
            for (var i = 0; i < allowedExtensions.Length; i++)
                configuration.AddInMemoryCollection(
                    new Dictionary<string, string?> { [$"FileStorage:AllowedFileExtensions:{i}"] = allowedExtensions[i] });
        }

        return new LocalFileStorageService(configuration.Build());
    }

    private static Stream ContentStream(string text) => new MemoryStream(Encoding.UTF8.GetBytes(text));

    [Fact]
    public async Task SaveThenReadThenDelete_RoundTrips()
    {
        var service = CreateService();
        var originalText = "hello, documents module";

        var storagePath = await service.SaveAsync(ContentStream(originalText), "notes.txt", "projects/p1");

        Assert.EndsWith(".txt", storagePath);
        Assert.StartsWith("projects/p1/", storagePath);
        // Original name is never used as the stored file name.
        Assert.DoesNotContain("notes", storagePath);

        await using (var read = await service.OpenReadAsync(storagePath))
        {
            Assert.NotNull(read);
            using var reader = new StreamReader(read!);
            Assert.Equal(originalText, await reader.ReadToEndAsync());
        }

        await service.DeleteAsync(storagePath);
        Assert.Null(await service.OpenReadAsync(storagePath));
    }

    [Fact]
    public async Task Save_WithNoSubFolder_StoresAtRoot()
    {
        var service = CreateService();

        var storagePath = await service.SaveAsync(ContentStream("x"), "a.pdf", subFolder: "");

        Assert.DoesNotContain("/", storagePath);
    }

    [Fact]
    public async Task Save_GeneratesAGuidNamedFile_NeverTheOriginalName()
    {
        var service = CreateService();

        var storagePath = await service.SaveAsync(ContentStream("x"), "sensitive-filename.pdf", "");
        var fileNameOnly = Path.GetFileNameWithoutExtension(storagePath);

        Assert.True(Guid.TryParseExact(fileNameOnly, "N", out _));
    }

    [Fact]
    public async Task Save_SubFolderWithDotDot_FailsClosed()
    {
        var service = CreateService();

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => service.SaveAsync(ContentStream("x"), "a.pdf", "../../etc/passwd"));

        Assert.Contains("subFolder", ex.Message + ex.ParamName);
        // Confirm nothing escaped the root - no file exists anywhere near the attempted target.
        Assert.False(Directory.Exists(Path.Combine(Path.GetTempPath(), "etc")));
    }

    [Fact]
    public async Task Save_SubFolderWithBackslashDotDot_FailsClosed()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.SaveAsync(ContentStream("x"), "a.pdf", @"..\..\windows\system32"));
    }

    [Fact]
    public async Task Save_OriginalFileNameWithPathSeparator_FailsClosed()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.SaveAsync(ContentStream("x"), "../../etc/passwd", ""));
    }

    [Fact]
    public async Task Save_OriginalFileNameWithDotDot_FailsClosed()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.SaveAsync(ContentStream("x"), "..evil.pdf".Replace("evil", ".."), ""));
    }

    [Fact]
    public async Task Save_BenignMessySubFolder_IsSanitizedNotRejected()
    {
        var service = CreateService();

        // Double slashes and a leading "./" are messy, not an attack - must not throw.
        var storagePath = await service.SaveAsync(ContentStream("x"), "a.pdf", "./projects//p1/");

        Assert.StartsWith("projects/p1/", storagePath);
    }

    [Fact]
    public async Task Save_ContentExceedingMaxUploadBytes_SeekableStream_RejectedBeforeAnyIo()
    {
        var service = CreateService(maxUploadBytes: 10);
        var content = ContentStream(new string('a', 100));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SaveAsync(content, "big.txt", ""));

        // Nothing was written - the whole point of enforcing the cap before streaming.
        Assert.False(Directory.Exists(_root) && Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories).Any());
    }

    /// <summary>A non-seekable stream can't be length-checked upfront - this is what forces the
    /// chunk-by-chunk enforcement during the copy, not just the seekable-stream fast path above.</summary>
    private sealed class NonSeekableStream(Stream inner) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
            inner.ReadAsync(buffer, offset, count, ct);
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public async Task Save_ContentExceedingMaxUploadBytes_NonSeekableStream_RejectedDuringCopy_AndPartialFileDeleted()
    {
        var service = CreateService(maxUploadBytes: 10);
        var content = new NonSeekableStream(ContentStream(new string('a', 1000)));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SaveAsync(content, "big.txt", ""));

        var leftoverFiles = Directory.Exists(_root)
            ? Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories).ToList()
            : [];
        Assert.Empty(leftoverFiles);
    }

    [Fact]
    public async Task Save_ContentAtExactlyMaxUploadBytes_Succeeds()
    {
        var service = CreateService(maxUploadBytes: 10);

        var storagePath = await service.SaveAsync(ContentStream(new string('a', 10)), "ok.txt", "");

        await using var read = await service.OpenReadAsync(storagePath);
        Assert.NotNull(read);
    }

    [Fact]
    public async Task Save_DisallowedExtension_IsRejected()
    {
        var service = CreateService(allowedExtensions: [".pdf"]);

        await Assert.ThrowsAsync<NotSupportedException>(
            () => service.SaveAsync(ContentStream("x"), "malware.exe", ""));
    }

    [Fact]
    public async Task Save_NoExtension_IsRejected_DenyByDefault()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<NotSupportedException>(
            () => service.SaveAsync(ContentStream("x"), "no-extension-at-all", ""));
    }

    [Theory]
    [InlineData(".exe")]
    [InlineData(".dll")]
    [InlineData(".bat")]
    [InlineData(".sh")]
    [InlineData(".ps1")]
    [InlineData(".cmd")]
    public async Task Save_KnownExecutableExtension_IsRejectedByTheDefaultAllowList(string extension)
    {
        var service = CreateService();

        await Assert.ThrowsAsync<NotSupportedException>(
            () => service.SaveAsync(ContentStream("x"), $"file{extension}", ""));
    }

    [Fact]
    public async Task OpenReadAsync_MissingFile_ReturnsNull()
    {
        var service = CreateService();

        var result = await service.OpenReadAsync("nonexistent/file.pdf");

        Assert.Null(result);
    }

    [Fact]
    public async Task OpenReadAsync_PathEscapingRoot_ReturnsNull_NotThrow()
    {
        var service = CreateService();

        // A storagePath value crafted to escape root - never produced by SaveAsync itself, but
        // OpenReadAsync must not trust an externally-supplied path either.
        var result = await service.OpenReadAsync("../../../../etc/passwd");

        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteAsync_MissingFile_DoesNotThrow()
    {
        var service = CreateService();

        await service.DeleteAsync("never/existed.pdf");
        // No exception is the assertion.
    }

    [Fact]
    public async Task DeleteAsync_PathEscapingRoot_DoesNotThrow_AndDeletesNothingOutsideRoot()
    {
        var service = CreateService();
        var outsideFile = Path.Combine(Path.GetTempPath(), $"dms-modular-outside-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(outsideFile, "must survive");

        try
        {
            await service.DeleteAsync("../" + Path.GetFileName(outsideFile));

            Assert.True(File.Exists(outsideFile));
        }
        finally
        {
            File.Delete(outsideFile);
        }
    }

    [Fact]
    public void MaxUploadBytes_DefaultsTo50Megabytes_WhenNotConfigured()
    {
        var service = CreateService();

        Assert.Equal(52_428_800, service.MaxUploadBytes);
    }
}
