using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using BuildingBlocks.Persistence;
using Crm.Contracts.Clients;
using Crm.Domain;
using Crm.Infrastructure.Decorators;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using Identity.Contracts;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Crm.Application.Features.Clients;

internal sealed class GetClientsQueryValidator : AbstractValidator<GetClientsQuery>
{
    public GetClientsQueryValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.PageNumber).InclusiveBetween(1, CrmPaging.MaxPageNumber);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
        RuleFor(x => x.Search).MaximumLength(200);
    }
}

/// <summary>
/// C-3 (LC-M3): the slim, paged client list.
///
/// <para><b>Visibility.</b> Without <c>crm.clients.read_all</c> the C-1 rule applies, in one batch:
/// the matching client ids (one query), then <see cref="CrmClientScope.VisibleAsync"/> (the ids' projects
/// and which of them the caller reaches), then the page. The filter runs in memory over the matching
/// ids (owner amendment M8). That is fine at today's size; past a few thousand clients per search it
/// wants a projects-side "my clients" read instead. A <c>read_all</c> caller skips all of that: the page
/// and the total are SQL (D-16 / C-F8).</para>
///
/// <para><b>Names.</b> The display name and email live on the portal login, so they come from one bulk
/// <see cref="IUserDirectory.GetManyAsync"/> call for the page. The login has no phone, so the row has
/// none, and <see cref="GetClientsQuery.Search"/> matches the CRM columns only (address and
/// representative name) — DEVIATION-C-1.</para>
/// </summary>
internal sealed class GetClientsQueryHandler(CrmDbContext db, CrmClientScope scope, IUserDirectory users)
    : IQueryHandler<GetClientsQuery, PagedResult<ClientListItemResponse>>
{
    public async Task<Result<PagedResult<ClientListItemResponse>>> HandleAsync(GetClientsQuery query, CancellationToken ct = default)
    {
        IQueryable<Client> matching = db.Clients.AsNoTracking();
        if (query.IsActive is { } isActive)
        {
            matching = matching.Where(c => c.IsActive == isActive);
        }

        if (Like.Contains(query.Search) is { } pattern)
        {
            matching = matching.Where(c =>
                EF.Functions.ILike(c.Address, pattern, Like.EscapeCharacter)
                || EF.Functions.ILike(c.RepresentativeName!, pattern, Like.EscapeCharacter));
        }

        // D-16 / C-F8: read_all sees every match, so the page and the total come straight from SQL.
        IQueryable<Client> visibleRows;
        int totalCount;
        if (scope.SeesAll)
        {
            visibleRows = matching;
            totalCount = await matching.CountAsync(ct);
        }
        else
        {
            List<Guid> matchingIds = await matching.Select(c => c.Id).ToListAsync(ct);
            Guid[] visibleIds = [.. await scope.VisibleAsync(matchingIds, query.UserId, ct)];
            visibleRows = db.Clients.AsNoTracking().Where(c => visibleIds.Contains(c.Id));
            totalCount = visibleIds.Length;
        }

        var rows = await visibleRows
            .OrderByDescending(c => c.CreatedAtUtc).ThenBy(c => c.Id)
            .Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize)
            .Select(c => new { c.Id, c.AppUserId, c.Address, c.JobTitle, c.RepresentativeName, c.IsActive, c.CreatedAtUtc })
            .ToListAsync(ct);

        var logins = (await users.GetManyAsync([.. rows.Select(r => r.AppUserId)], ct)).ToDictionary(u => u.Id);
        List<ClientListItemResponse> items = [.. rows.Select(r =>
        {
            UserSummary? login = logins.GetValueOrDefault(r.AppUserId);
            return new ClientListItemResponse(
                r.Id, login?.DisplayName, login?.Email, r.Address, r.JobTitle, r.RepresentativeName, r.IsActive, r.CreatedAtUtc,
                login?.PhoneNumber);
        })];

        return new PagedResult<ClientListItemResponse>(items, query.PageNumber, query.PageSize, totalCount);
    }
}
