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

        leads = LeadFilters.Apply(leads, query.OwnerId, query.Search);

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
}

internal sealed class GetCrmLeadStatusCountsQueryValidator : AbstractValidator<GetCrmLeadStatusCountsQuery>
{
    public GetCrmLeadStatusCountsQueryValidator() => RuleFor(x => x.Search).MaximumLength(200);
}

/// <summary>One <c>GROUP BY</c> over the same owner and search filters as the paged list
/// (<see cref="LeadFilters"/>), so the counts and the list's totals always agree.</summary>
internal sealed class GetCrmLeadStatusCountsQueryHandler(CrmDbContext db)
    : IQueryHandler<GetCrmLeadStatusCountsQuery, CrmLeadStatusCountsResponse>
{
    public async Task<Result<CrmLeadStatusCountsResponse>> HandleAsync(GetCrmLeadStatusCountsQuery query, CancellationToken ct = default)
    {
        Dictionary<LeadStatus, int> counts = await LeadFilters.Apply(db.CrmLeads.AsNoTracking(), query.OwnerId, query.Search)
            .GroupBy(l => l.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Status, g => g.Count, ct);

        return new CrmLeadStatusCountsResponse(
            counts.Values.Sum(),
            counts.GetValueOrDefault(LeadStatus.New),
            counts.GetValueOrDefault(LeadStatus.Contacted),
            counts.GetValueOrDefault(LeadStatus.Qualified),
            counts.GetValueOrDefault(LeadStatus.Unqualified),
            counts.GetValueOrDefault(LeadStatus.Converted));
    }
}

/// <summary>The owner and search filters shared by the paged lead list and the status counts. Search is a
/// case-insensitive contains on first name, last name, email and phone, with <c>%</c> and <c>_</c> taken
/// literally.</summary>
internal static class LeadFilters
{
    public static IQueryable<CrmLead> Apply(IQueryable<CrmLead> leads, Guid? ownerId, string? search)
    {
        if (ownerId is { } owner)
        {
            leads = leads.Where(l => l.OwnerId == owner);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            string pattern = $"%{EscapeLike(search.Trim())}%";
            leads = leads.Where(l =>
                EF.Functions.ILike(l.FirstName, pattern, @"\")
                || EF.Functions.ILike(l.LastName, pattern, @"\")
                || EF.Functions.ILike(l.Email!, pattern, @"\")
                || EF.Functions.ILike(l.Phone!, pattern, @"\"));
        }

        return leads;
    }

    private static string EscapeLike(string value) =>
        value.Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);
}
