namespace Documents.Contracts;

/// <summary>
/// Ported as-is from <c>../backend/Application/Interfaces/IFileStorageService.cs</c>. Implemented by
/// <c>LocalFileStorageService</c> in <c>Documents.Infrastructure</c> (G7-05) — local disk only for now;
/// this interface is the seam for swapping to cloud blob storage later (an ops decision, not this
/// phase's work — see <c>docs/low-resource-hosting.md</c>).
/// </summary>
public interface IFileStorageService
{
    /// <summary>Configured via <c>FileStorage:MaxUploadBytes</c>, default 52,428,800 (50 MB).</summary>
    long MaxUploadBytes { get; }

    /// <summary>
    /// Streams <paramref name="content"/> to disk under a sanitized <paramref name="subFolder"/>, named
    /// <c>{Guid:N}{extension}</c> — never <paramref name="originalFileName"/> itself, which is preserved
    /// only in the caller's own database row. Returns the forward-slash relative path stored there.
    /// </summary>
    Task<string> SaveAsync(
        Stream content, string originalFileName, string subFolder, CancellationToken ct = default);

    /// <summary>Null if <paramref name="storagePath"/> resolves outside the configured root, or the
    /// file doesn't exist — never throws for a missing/invalid path.</summary>
    Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct = default);

    /// <summary>Best-effort; never throws, including when the file is already missing.</summary>
    Task DeleteAsync(string storagePath, CancellationToken ct = default);
}
