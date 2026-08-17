namespace BuildingBlocks.Auditing;

/// <summary>
/// Who the caller is and where the request came from, as seen by the audit interceptor. Every member
/// is nullable because every member is genuinely absent on a non-HTTP write.
///
/// <para><b>Deliberately no <c>UserName</c>.</b> The backend captures one, but no token issued in this
/// repo carries a name claim — neither <c>JwtProvider</c> nor the test issuer emits one — so the column
/// would be permanently null. <c>UserEmail</c> is kept because <c>JwtProvider</c> does emit
/// <c>email</c>, making it a real snapshot for interactive writes and honestly null for background
/// ones.</para>
/// </summary>
public sealed record AuditContext(
    string? UserId,
    string? UserEmail,
    string? IpAddress,
    string? UserAgent,
    string? RequestPath,
    string? RequestMethod)
{
    /// <summary>No ambient caller — seeding, the outbox processor's background scope, a periodic job,
    /// or any other write that did not originate in a request.</summary>
    public static readonly AuditContext None = new(null, null, null, null, null, null);
}

/// <summary>
/// Supplies the ambient <see cref="AuditContext"/>. Interface only — the HTTP-backed implementation
/// lives in <c>Api</c>, exactly as <see cref="ICurrentUser"/> does, and for a harder reason than
/// convention: architecture rule 8b is a raw text scan of <c>BuildingBlocks.csproj</c> for the string
/// "AspNetCore", so BuildingBlocks cannot reference ASP.NET Core at all. This split is not the tidy
/// shape, it is the only shape that compiles.
/// </summary>
public interface IAuditContextAccessor
{
    AuditContext GetCurrent();
}
