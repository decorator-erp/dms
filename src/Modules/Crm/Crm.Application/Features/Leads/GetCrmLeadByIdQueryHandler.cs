using BuildingBlocks.Messaging;
using Crm.Contracts.Leads;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Crm.Application.Features.Leads;

internal sealed class GetCrmLeadByIdQueryHandler(CrmDbContext db) : IQueryHandler<GetCrmLeadByIdQuery, CrmLeadResponse>
{
    internal static Func<CrmLead, CrmLeadResponse> ToResponse() => l => new CrmLeadResponse(
        l.Id, l.FirstName, l.LastName, l.Email, l.Phone, CrmEnumMapping.ToContracts(l.Source),
        CrmEnumMapping.ToContracts(l.Status), l.OwnerId, l.Notes, l.ConvertedClientId, l.ConvertedAtUtc);

    public async Task<Result<CrmLeadResponse>> HandleAsync(GetCrmLeadByIdQuery query, CancellationToken ct = default)
    {
        CrmLead? lead = await db.CrmLeads.AsNoTracking().FirstOrDefaultAsync(l => l.Id == query.LeadId, ct);
        if (lead is null)
        {
            return Result.Failure<CrmLeadResponse>(CrmLeadErrors.NotFound);
        }

        return ToResponse()(lead);
    }
}
