using BuildingBlocks.Messaging;
using Crm.Contracts.Clients;
using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Crm.Application.Features.Clients;

internal sealed class GetClientsQueryHandler(CrmDbContext db)
    : IQueryHandler<GetClientsQuery, IReadOnlyList<ClientResponse>>
{
    public async Task<Result<IReadOnlyList<ClientResponse>>> HandleAsync(GetClientsQuery query, CancellationToken ct = default)
    {
        List<ClientResponse> clients = await db.Clients.AsNoTracking()
            .OrderBy(c => c.Address)
            .Select(c => new ClientResponse(
                c.Id, c.AppUserId, c.Address, c.Notes, c.JobTitle, c.MaritalStatus, c.RepresentativeName,
                c.Children.Select(ch => new ClientChildResponse(ch.Id, ch.Gender, ch.Age)).ToList(), c.IsActive))
            .ToListAsync(ct);

        return clients;
    }
}
