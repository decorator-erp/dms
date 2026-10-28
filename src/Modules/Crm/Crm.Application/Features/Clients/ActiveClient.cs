using Crm.Domain;
using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Crm.Application.Features.Clients;

/// <summary>C-5 (LC-M5): the "is this client still taking writes" check the contact and note handlers
/// share — 404 for an unknown client, 409 <see cref="ClientErrors.Inactive"/> for a deactivated one.</summary>
internal static class ActiveClient
{
    public static async Task<Result> EnsureAsync(CrmDbContext db, Guid clientId, CancellationToken ct)
    {
        bool? isActive = await db.Clients.AsNoTracking()
            .Where(c => c.Id == clientId)
            .Select(c => (bool?)c.IsActive)
            .FirstOrDefaultAsync(ct);

        return isActive switch
        {
            null => ClientErrors.NotFound,
            false => ClientErrors.Inactive,
            true => Result.Success(),
        };
    }
}
