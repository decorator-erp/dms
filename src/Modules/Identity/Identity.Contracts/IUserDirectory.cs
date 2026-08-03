namespace Identity.Contracts;

public interface IUserDirectory
{
    Task<UserSummary?> GetAsync(string userId, CancellationToken ct);
    Task<IReadOnlyList<UserSummary>> GetManyAsync(IReadOnlyCollection<string> ids, CancellationToken ct);
}

public sealed record UserSummary(string Id, string Email, string? DisplayName, bool IsActive);
