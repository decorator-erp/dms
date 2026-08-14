using BuildingBlocks.Messaging;
using Crm.Contracts.Leads;
using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Crm.Application.Features.Leads;

internal sealed class GetCrmLeadsQueryHandler(CrmDbContext db)
    : IQueryHandler<GetCrmLeadsQuery, IReadOnlyList<CrmLeadResponse>>
{
    public async Task<Result<IReadOnlyList<CrmLeadResponse>>> HandleAsync(GetCrmLeadsQuery query, CancellationToken ct = default)
    {
        List<CrmLeadResponse> leads = await db.CrmLeads.AsNoTracking()
            .OrderByDescending(l => l.CreatedAtUtc)
            .Select(l => new CrmLeadResponse(
                l.Id, l.FirstName, l.LastName, l.Email, l.Phone, CrmEnumMapping.ToContracts(l.Source),
                CrmEnumMapping.ToContracts(l.Status), l.OwnerId, l.Notes, l.ConvertedClientId, l.ConvertedAtUtc))
            .ToListAsync(ct);

        return leads;
    }
}
