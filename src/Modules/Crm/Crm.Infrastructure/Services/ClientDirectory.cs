using Crm.Contracts;
using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Crm.Infrastructure.Services;

/// <summary><see cref="IClientDirectory"/>'s implementation — projects straight to the Contracts DTO
/// via <c>Select</c>, never load-then-map, matching <c>DocumentQueries</c>'s own precedent. First
/// caller is <c>Projects.Application</c>'s <c>CreateProjectCommandHandler</c> (G9-13), which validates
/// <c>Project.ClientId</c> through <see cref="ExistAsync"/> — closing the Phase 2 deferral.</summary>
internal sealed class ClientDirectory(CrmDbContext db) : IClientDirectory
{
    public Task<ClientSummary?> GetAsync(Guid clientId, CancellationToken ct) =>
        db.Clients.AsNoTracking()
            .Where(c => c.Id == clientId)
            .Select(c => new ClientSummary(c.Id, c.AppUserId, c.Address, c.RepresentativeName, c.IsActive))
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<ClientSummary>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        return await db.Clients.AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .Select(c => new ClientSummary(c.Id, c.AppUserId, c.Address, c.RepresentativeName, c.IsActive))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Guid>> ExistAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        return await db.Clients.AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .Select(c => c.Id)
            .ToListAsync(ct);
    }

    public Task<ClientSummary?> ResolveByAppUserIdAsync(string appUserId, CancellationToken ct) =>
        db.Clients.AsNoTracking()
            .Where(c => c.AppUserId == appUserId)
            .Select(c => new ClientSummary(c.Id, c.AppUserId, c.Address, c.RepresentativeName, c.IsActive))
            .FirstOrDefaultAsync(ct);
}
