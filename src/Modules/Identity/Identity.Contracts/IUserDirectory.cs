namespace Identity.Contracts;

public interface IUserDirectory
{
    Task<UserSummary?> GetAsync(string userId, CancellationToken ct);
    Task<IReadOnlyList<UserSummary>> GetManyAsync(IReadOnlyCollection<string> ids, CancellationToken ct);
}

/// <summary><see cref="PhoneNumber"/> is the login's own phone (a client's is set when the client is created).</summary>
public sealed record UserSummary(string Id, string Email, string? DisplayName, bool IsActive, string? PhoneNumber = null);
