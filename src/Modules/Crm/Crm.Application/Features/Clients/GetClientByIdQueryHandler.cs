using BuildingBlocks.Messaging;
using Crm.Contracts.Clients;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Crm.Application.Features.Clients;

internal sealed class GetClientByIdQueryHandler(CrmDbContext db) : IQueryHandler<GetClientByIdQuery, ClientResponse>
{
    internal static Func<Client, ClientResponse> ToResponse() => c => new ClientResponse(
        c.Id, c.AppUserId, c.Address, c.Notes, c.JobTitle, c.MaritalStatus, c.RepresentativeName,
        c.Children.Select(ch => new ClientChildResponse(ch.Id, ch.Gender, ch.Age)).ToList(), c.IsActive);

    public async Task<Result<ClientResponse>> HandleAsync(GetClientByIdQuery query, CancellationToken ct = default)
    {
        Client? client = await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == query.ClientId, ct);
        if (client is null)
        {
            return Result.Failure<ClientResponse>(ClientErrors.NotFound);
        }

        return ToResponse()(client);
    }
}
