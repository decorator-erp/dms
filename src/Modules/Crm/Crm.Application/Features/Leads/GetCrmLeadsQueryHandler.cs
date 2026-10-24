using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using Crm.Contracts.Leads;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Crm.Application.Features.Leads;

internal sealed class GetCrmLeadsQueryValidator : AbstractValidator<GetCrmLeadsQuery>
{
    public GetCrmLeadsQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.Search).MaximumLength(200);
    }
}

internal sealed class GetCrmLeadsQueryHandler(CrmDbContext db)
    : IQueryHandler<GetCrmLeadsQuery, PagedResult<CrmLeadResponse>>
{
    public async Task<Result<PagedResult<CrmLeadResponse>>> HandleAsync(GetCrmLeadsQuery query, CancellationToken ct = default)
    {
        IQueryable<CrmLead> leads = db.CrmLeads.AsNoTracking();

        if (query.Status is { } status)
        {
            LeadStatus domainStatus = CrmEnumMapping.ToDomain(status);
            leads = leads.Where(l => l.Status == domainStatus);
        }

        if (query.OwnerId is { } ownerId)
        {
            leads = leads.Where(l => l.OwnerId == ownerId);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            string pattern = $"%{EscapeLike(query.Search.Trim())}%";
            leads = leads.Where(l =>
                EF.Functions.ILike(l.FirstName, pattern, @"\")
                || EF.Functions.ILike(l.LastName, pattern, @"\")
                || EF.Functions.ILike(l.Email!, pattern, @"\")
                || EF.Functions.ILike(l.Phone!, pattern, @"\"));
        }

        int totalCount = await leads.CountAsync(ct);

        List<CrmLeadResponse> items = await leads
            .OrderByDescending(l => l.CreatedAtUtc)
            .ThenBy(l => l.Id)
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(l => new CrmLeadResponse(
                l.Id, l.FirstName, l.LastName, l.Email, l.Phone, CrmEnumMapping.ToContracts(l.Source),
                CrmEnumMapping.ToContracts(l.Status), l.OwnerId, l.Notes, l.ConvertedClientId, l.ConvertedAtUtc))
            .ToListAsync(ct);

        return new PagedResult<CrmLeadResponse>(items, query.PageNumber, query.PageSize, totalCount);
    }

    private static string EscapeLike(string value) =>
        value.Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);
}
