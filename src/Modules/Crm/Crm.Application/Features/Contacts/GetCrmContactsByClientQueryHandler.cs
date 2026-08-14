using BuildingBlocks.Messaging;
using Crm.Contracts.Contacts;
using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Crm.Application.Features.Contacts;

internal sealed class GetCrmContactsByClientQueryHandler(CrmDbContext db)
    : IQueryHandler<GetCrmContactsByClientQuery, IReadOnlyList<CrmContactResponse>>
{
    public async Task<Result<IReadOnlyList<CrmContactResponse>>> HandleAsync(
        GetCrmContactsByClientQuery query, CancellationToken ct = default)
    {
        List<CrmContactResponse> contacts = await db.CrmContacts.AsNoTracking()
            .Where(c => c.ClientId == query.ClientId)
            .OrderByDescending(c => c.IsPrimary).ThenBy(c => c.LastName)
            .Select(c => new CrmContactResponse(
                c.Id, c.ClientId, c.FirstName, c.LastName, c.Email, c.Phone, c.Mobile,
                CrmEnumMapping.ToContracts(c.Role), c.IsPrimary, c.Notes))
            .ToListAsync(ct);

        return contacts;
    }
}
