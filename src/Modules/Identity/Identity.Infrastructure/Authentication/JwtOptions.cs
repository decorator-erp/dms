using System.ComponentModel.DataAnnotations;

namespace Identity.Infrastructure.Authentication;

internal sealed class JwtOptions
{
    public const string SectionName = "JwtSettings";

    [Required, MinLength(32)]
    public string Key { get; init; } = null!;

    [Required]
    public string Issuer { get; init; } = null!;

    [Required]
    public string Audience { get; init; } = null!;

    [Required]
    public int ExpiryMinutes { get; init; } = 60;

    /// <summary>Not in the backend's <c>JwtOptions</c> — the 14-day refresh-token lifetime was a
    /// hard-coded field on <c>AuthService</c> there. Made configurable here since
    /// <c>ApplicationUser.IssueRefreshToken</c> (G1-02) takes the lifetime as a parameter rather than
    /// assuming a fixed value.</summary>
    [Required]
    public int RefreshTokenExpiryDays { get; init; } = 14;
}
