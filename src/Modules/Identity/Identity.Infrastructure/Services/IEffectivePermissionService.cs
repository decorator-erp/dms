namespace Identity.Infrastructure.Services;

internal interface IEffectivePermissionService
{
    Task<IEnumerable<string>> ResolveAsync(string userId, CancellationToken ct = default);
}
