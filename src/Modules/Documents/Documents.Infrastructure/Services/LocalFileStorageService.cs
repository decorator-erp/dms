using Documents.Contracts;
using Microsoft.Extensions.Configuration;

namespace Documents.Infrastructure.Services;

/// <summary>
/// Ported from the backend's <c>LocalFileStorageService</c> — local disk only for now
/// (<c>IFileStorageService</c> is the seam for cloud blob storage later, an ops decision, not this
/// phase's work). <c>FileStorage:RootPath</c> (default <c>uploads</c>), <c>FileStorage:MaxUploadBytes</c>
/// (default 52,428,800 = 50 MB), name generation (<c>{Guid:N}{extension}</c>, never the original name),
/// and <c>FileMode.CreateNew</c> (a GUID name already prevents collisions) all match the backend exactly.
///
/// <para>
/// G7-05 hardening the backend lacks, all fail-closed:
/// <list type="bullet">
/// <item><b>Path traversal.</b> <paramref name="subFolder"/> is sanitized for benign mess (normalized
/// separators, empty/<c>.</c> segments dropped — the backend's own logic for that part) but a <c>..</c>
/// segment is REJECTED outright rather than silently stripped and absorbed into a different, unintended
/// path, unlike the backend. The FINAL resolved absolute path is then also asserted to still sit under
/// the configured root regardless — a backstop that fires even if the reject-on-`..` check itself has a
/// bug. <paramref name="originalFileName"/> is rejected outright if it contains a path separator or a
/// <c>..</c> segment, rather than silently discarding everything but the extension and hoping that's
/// safe. The root-containment check itself is also fixed here versus the backend's own version: a bare
/// <c>fullPath.StartsWith(root)</c> string check (which the backend uses) treats a sibling directory
/// like <c>uploads-evil</c> as "under" <c>uploads</c>, since <c>"uploads-evil".StartsWith("uploads")</c>
/// is true. This version requires either an exact match or a directory-separator-bounded prefix.</item>
/// <item><b>Size cap enforced DURING the copy, not after.</b> A seekable stream with a known
/// <see cref="Stream.Length"/> over the cap is rejected before any I/O. Every stream, seekable or not,
/// is also checked chunk-by-chunk while copying — the moment the running total would exceed
/// <see cref="MaxUploadBytes"/>, the copy stops, the partial file is deleted, and the call fails. An
/// oversized file is never allowed to finish writing and get rejected afterward.</item>
/// <item><b>Extension allow-list, deny-by-default.</b> There is no <c>contentType</c> parameter on
/// <see cref="IFileStorageService.SaveAsync"/> (ported as-is from the backend's own interface, G7-01),
/// so this validates the file EXTENSION rather than an HTTP content-type header — the same practical
/// concern ("don't let someone upload an executable"), checked against
/// <c>FileStorage:AllowedFileExtensions</c> (falls back to a hardcoded document/image/archive list,
/// never `.exe`/`.dll`/`.bat`/`.sh`/`.ps1`/etc.) if config doesn't override it. An extension not on the
/// list — including no extension at all — is rejected.</item>
/// </list>
/// </para>
/// </summary>
internal sealed class LocalFileStorageService : IFileStorageService
{
    private static readonly IReadOnlySet<string> DefaultAllowedExtensions = new HashSet<string>(
        [
            ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tif", ".tiff",
            ".dwg", ".dxf",
            ".zip", ".rar", ".7z",
            ".txt", ".csv",
        ],
        StringComparer.OrdinalIgnoreCase);

    private const int CopyBufferSize = 81_920;

    private readonly string _rootPath;
    private readonly IReadOnlySet<string> _allowedExtensions;

    public long MaxUploadBytes { get; }

    public LocalFileStorageService(IConfiguration cfg)
    {
        string? configuredRoot = cfg["FileStorage:RootPath"];
        string root = string.IsNullOrWhiteSpace(configuredRoot) ? "uploads" : configuredRoot;
        _rootPath = Path.IsPathRooted(root) ? root : Path.Combine(Directory.GetCurrentDirectory(), root);
        Directory.CreateDirectory(_rootPath);

        MaxUploadBytes = long.TryParse(cfg["FileStorage:MaxUploadBytes"], out long configuredMax) && configuredMax > 0
            ? configuredMax
            : 52_428_800;

        string[]? configuredExtensions = cfg.GetSection("FileStorage:AllowedFileExtensions").Get<string[]>();
        _allowedExtensions = configuredExtensions is { Length: > 0 }
            ? new HashSet<string>(configuredExtensions, StringComparer.OrdinalIgnoreCase)
            : DefaultAllowedExtensions;
    }

    public async Task<string> SaveAsync(
        Stream content, string originalFileName, string subFolder, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(originalFileName))
        {
            throw new ArgumentException("A file name is required.", nameof(originalFileName));
        }

        if (originalFileName.IndexOfAny(['/', '\\']) >= 0 || originalFileName.Contains(".."))
        {
            throw new ArgumentException(
                "The file name may not contain path separators or '..'.", nameof(originalFileName));
        }

        string extension = Path.GetExtension(originalFileName).ToLowerInvariant();
        if (!_allowedExtensions.Contains(extension))
        {
            throw new NotSupportedException($"File extension '{extension}' is not allowed.");
        }

        if (content.CanSeek && content.Length > MaxUploadBytes)
        {
            throw new InvalidOperationException(
                $"File exceeds the maximum allowed size of {MaxUploadBytes} bytes.");
        }

        string sanitizedSubFolder = SanitizeSubFolder(subFolder);
        string storedFileName = $"{Guid.NewGuid():N}{extension}";
        string relativePath = sanitizedSubFolder.Length == 0
            ? storedFileName
            : $"{sanitizedSubFolder}/{storedFileName}";

        string fullPath = ResolveUnderRoot(relativePath)
            ?? throw new InvalidOperationException("Resolved storage path escapes the configured root.");

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        bool succeeded = false;
        try
        {
            await using (var fileStream = new FileStream(
                fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                byte[] buffer = new byte[CopyBufferSize];
                long totalWritten = 0;
                int read;
                while ((read = await content.ReadAsync(buffer, ct)) > 0)
                {
                    totalWritten += read;
                    if (totalWritten > MaxUploadBytes)
                    {
                        throw new InvalidOperationException(
                            $"File exceeds the maximum allowed size of {MaxUploadBytes} bytes.");
                    }

                    await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
                }
            }

            succeeded = true;
        }
        finally
        {
            // Delete the partial file on any failure mid-copy (size cap exceeded, cancellation, I/O
            // error) - never leave a truncated, oversized-attempt file behind.
            if (!succeeded && File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }

        return relativePath;
    }

    public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct = default)
    {
        string? fullPath = TryResolveExistingUnderRoot(storagePath);

        Stream? stream = fullPath is null
            ? null
            : new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);

        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string storagePath, CancellationToken ct = default)
    {
        try
        {
            string? fullPath = TryResolveExistingUnderRoot(storagePath);
            if (fullPath is not null)
            {
                File.Delete(fullPath);
            }
        }
        catch
        {
            // Best-effort, matching the backend exactly - never throws, including on a missing file.
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Normalizes separators and drops merely messy segments (empty, <c>.</c>) — a stray double slash
    /// or a leading <c>./</c> is not an attack. A <c>..</c> segment IS: unlike the backend, which
    /// silently strips it and continues, this rejects outright. "Sanitize, then assert the resolved
    /// path is still under root" (the plan's own wording) describes two distinct defenses, not one —
    /// sanitizing handles benign mess, this explicit reject is what makes a traversal ATTEMPT actually
    /// fail closed instead of being quietly absorbed into a different, safe-but-unintended path.
    /// </summary>
    private static string SanitizeSubFolder(string? subFolder)
    {
        if (string.IsNullOrWhiteSpace(subFolder))
        {
            return string.Empty;
        }

        string[] segments = subFolder.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Any(s => s == ".."))
        {
            throw new ArgumentException(
                "The sub-folder path may not contain '..' segments.", nameof(subFolder));
        }

        IEnumerable<string> safeSegments = segments.Where(s => s != ".");
        return string.Join('/', safeSegments);
    }

    /// <summary>Resolves a relative path under the root, returning null if the result would escape it —
    /// the fail-closed backstop, independent of whatever sanitization already happened upstream.</summary>
    private string? ResolveUnderRoot(string relativePath)
    {
        string fullPath = Path.GetFullPath(
            Path.Combine(_rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        return IsUnderRoot(fullPath) ? fullPath : null;
    }

    private string? TryResolveExistingUnderRoot(string storagePath)
    {
        if (string.IsNullOrWhiteSpace(storagePath))
        {
            return null;
        }

        string? fullPath = ResolveUnderRoot(storagePath);
        return fullPath is not null && File.Exists(fullPath) ? fullPath : null;
    }

    /// <summary>Directory-separator-bounded containment check — a plain <c>StartsWith</c> (the backend's
    /// own version) would wrongly treat a sibling directory like <c>uploads-evil</c> as "under"
    /// <c>uploads</c>.</summary>
    private bool IsUnderRoot(string fullPath)
    {
        string normalizedRoot = _rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return fullPath.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
