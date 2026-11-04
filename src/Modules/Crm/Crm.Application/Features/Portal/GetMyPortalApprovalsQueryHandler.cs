using Approvals.Contracts;
using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using Crm.Application.Features.Briefs;
using Crm.Contracts;
using Crm.Contracts.Portal;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Projects.Contracts;
using SharedKernel;

namespace Crm.Application.Features.Portal;

/// <summary>B1.3: same paging rules as the other portal lists.</summary>
internal sealed class GetMyPortalApprovalsQueryValidator : AbstractValidator<GetMyPortalApprovalsQuery>
{
    public GetMyPortalApprovalsQueryValidator()
    {
        RuleFor(x => x.PageNumber).InclusiveBetween(1, CrmPaging.MaxPageNumber);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
    }
}

/// <summary>
/// B1.3 (C5): the client's approvals inbox. The page is cut on the Approvals side
/// (<see cref="IClientApprovalReader"/>: approvals naming the caller as a Client approver, CRM's three
/// subject types), then enriched here in a fixed number of batch reads — entries, briefs, brief rounds,
/// items, periods, spaces, then projects and stages from <see cref="IProjectDirectory"/> — never one per row
/// (an empty id set still costs its one cheap query; the count stays constant).
///
/// <para>A row whose subject is gone, or whose project is not the caller's client's, is dropped and logged at
/// Warning (the approver set and the project's client disagree — an office reassignment or bad data); an
/// inactive project's row is dropped quietly, as the decide answers 404 for it. Dropping happens after the
/// page is cut, so <c>TotalCount</c> may over-count by the dropped rows.</para>
/// </summary>
internal sealed class GetMyPortalApprovalsQueryHandler(
    CrmDbContext db,
    ICrmAccessGuard guard,
    IClientApprovalReader reader,
    IProjectDirectory projectDirectory,
    ILogger<GetMyPortalApprovalsQueryHandler> logger)
    : IQueryHandler<GetMyPortalApprovalsQuery, PagedResult<PortalApprovalResponse>>
{
    private static readonly ApprovalSubjectType[] CrmSubjects =
        [ApprovalSubjectType.DesignBrief, ApprovalSubjectType.RequirementItem, ApprovalSubjectType.BriefSpace];

    public async Task<Result<PagedResult<PortalApprovalResponse>>> HandleAsync(
        GetMyPortalApprovalsQuery query, CancellationToken ct = default)
    {
        bool? decided = query.Status?.Trim().ToUpperInvariant() switch
        {
            null or "" or "PENDING" => false,
            "DECIDED" => true,
            _ => null,
        };
        if (decided is null)
        {
            return Result.Failure<PagedResult<PortalApprovalResponse>>(CrmPortalErrors.InvalidStatus);
        }

        Result<Guid> clientId = await PortalClients.ResolveClientIdAsync(guard, query.AppUserId, ct);
        if (clientId.IsFailure)
        {
            return Result.Failure<PagedResult<PortalApprovalResponse>>(clientId.Error);
        }

        PagedResult<ClientApprovalRow> page = await reader.GetForClientApproverAsync(
            query.AppUserId, CrmSubjects, decided.Value, query.PageNumber, query.PageSize, ct);
        if (page.Items.Count == 0)
        {
            return new PagedResult<PortalApprovalResponse>([], page.PageNumber, page.PageSize, page.TotalCount);
        }

        Subjects subjects = await LoadSubjectsAsync(page.Items, ct);
        IReadOnlyList<ProjectSummary> projects = await projectDirectory.GetProjectsAsync(
            [.. subjects.ById.Values.Select(s => s.ProjectId).Distinct()], ct);
        Guid[] stageIds = [.. subjects.ById.Values.Where(s => s.StageId.HasValue).Select(s => s.StageId!.Value).Distinct()];
        IReadOnlyList<StageSummary> stages = stageIds.Length == 0 ? [] : await projectDirectory.GetStagesAsync(stageIds, ct);
        var projectById = projects.ToDictionary(p => p.Id);
        var stageNameById = stages.ToDictionary(s => s.Id, s => s.Name);

        List<PortalApprovalResponse> items = [];
        foreach (ClientApprovalRow row in page.Items)
        {
            if (!subjects.ById.TryGetValue(row.Id, out Subject? subject)
                || !projectById.TryGetValue(subject.ProjectId, out ProjectSummary? project)
                || project.ClientId != clientId.Value)
            {
                logger.LogWarning(
                    "Dropping portal approval {ApprovalId} ({SubjectType} {SubjectId}) for client {ClientId}: "
                    + "its subject is gone or its project is not the client's",
                    row.Id, row.SubjectType, row.SubjectId, clientId.Value);
                continue;
            }

            if (!project.IsActive)
            {
                continue;
            }

            items.Add(new PortalApprovalResponse(
                row.Id, (PortalApprovalSubjectType)(int)row.SubjectType, row.SubjectId, project.Id, project.Name,
                subject.BriefId, subject.StageId,
                subject.StageId is { } stageId ? stageNameById.GetValueOrDefault(stageId) : null,
                subject.Round, subject.Title, row.RequestedAtUtc, row.DueAtUtc?.UtcDateTime,
                row.Decision is { } d ? (PortalApprovalDecision)(int)d : null, row.DecidedAtUtc?.UtcDateTime,
                row.DecidedByUserId == query.AppUserId ? row.Comment : null));
        }

        return new PagedResult<PortalApprovalResponse>(items, page.PageNumber, page.PageSize, page.TotalCount);
    }

    private async Task<Subjects> LoadSubjectsAsync(IReadOnlyList<ClientApprovalRow> rows, CancellationToken ct)
    {
        Guid[] briefSubjectIds = SubjectIds(rows, ApprovalSubjectType.DesignBrief);
        Guid[] entryIds = SubjectIds(rows, ApprovalSubjectType.BriefSpace);
        Guid[] itemIds = SubjectIds(rows, ApprovalSubjectType.RequirementItem);

        var entries = await db.CrmBriefSpaces.AsNoTracking()
            .Where(e => entryIds.Contains(e.Id))
            .Select(e => new { e.Id, e.BriefId, e.SpaceId, e.SendCount })
            .ToListAsync(ct);

        Guid[] briefIds = [.. briefSubjectIds.Concat(entries.Select(e => e.BriefId)).Distinct()];
        var briefs = await db.CrmDesignBriefs.AsNoTracking()
            .Where(b => briefIds.Contains(b.Id))
            .Select(b => new { b.Id, b.ProjectId, b.ProjectStageId })
            .ToListAsync(ct);
        var briefRounds = await db.CrmBriefHeadSnapshots.AsNoTracking()
            .Where(s => briefSubjectIds.Contains(s.BriefId))
            .GroupBy(s => s.BriefId)
            .Select(g => new { BriefId = g.Key, MaxRound = g.Max(s => s.Round) })
            .ToListAsync(ct);

        var items = await db.CrmRequirementItems.AsNoTracking()
            .Where(i => itemIds.Contains(i.Id))
            .Select(i => new { i.Id, i.ProjectId, i.PeriodId, i.Title, i.SendCount })
            .ToListAsync(ct);
        Guid[] periodIds = [.. items.Select(i => i.PeriodId).Distinct()];
        var periodStages = await db.CrmRequirementPeriods.AsNoTracking()
            .Where(p => periodIds.Contains(p.Id))
            .Select(p => new { p.Id, p.ProjectStageId })
            .ToListAsync(ct);

        Guid[] spaceIds = [.. entries.Where(e => e.SpaceId.HasValue).Select(e => e.SpaceId!.Value).Distinct()];
        var spaces = await db.CrmSpaces.AsNoTracking()
            .Where(s => spaceIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Name })
            .ToListAsync(ct);

        var briefById = briefs.ToDictionary(b => b.Id);
        var maxRoundByBrief = briefRounds.ToDictionary(r => r.BriefId, r => r.MaxRound);
        var entryById = entries.ToDictionary(e => e.Id);
        var itemById = items.ToDictionary(i => i.Id);
        var stageByPeriod = periodStages.ToDictionary(p => p.Id, p => p.ProjectStageId);
        var spaceNameById = spaces.ToDictionary(s => s.Id, s => s.Name);

        Dictionary<Guid, Subject> byApproval = [];
        foreach (ClientApprovalRow row in rows)
        {
            Subject? subject = row.SubjectType switch
            {
                ApprovalSubjectType.DesignBrief when briefById.TryGetValue(row.SubjectId, out var brief) => new Subject(
                    brief.ProjectId, brief.Id, brief.ProjectStageId,
                    RoundOf(row, maxRoundByBrief.GetValueOrDefault(brief.Id)), Title: null),
                ApprovalSubjectType.BriefSpace when entryById.TryGetValue(row.SubjectId, out var entry)
                    && briefById.TryGetValue(entry.BriefId, out var entryBrief) => new Subject(
                    entryBrief.ProjectId, entryBrief.Id, entryBrief.ProjectStageId, RoundOf(row, entry.SendCount),
                    entry.SpaceId is { } spaceId ? spaceNameById.GetValueOrDefault(spaceId) : null),
                ApprovalSubjectType.RequirementItem when itemById.TryGetValue(row.SubjectId, out var item) => new Subject(
                    item.ProjectId, BriefId: null, stageByPeriod.GetValueOrDefault(item.PeriodId),
                    RoundOf(row, item.SendCount), item.Title),
                _ => null,
            };
            if (subject is not null)
            {
                byApproval[row.Id] = subject;
            }
        }

        return new Subjects(byApproval);
    }

    private static Guid[] SubjectIds(IReadOnlyList<ClientApprovalRow> rows, ApprovalSubjectType type) =>
        [.. rows.Where(r => r.SubjectType == type).Select(r => r.SubjectId).Distinct()];

    /// <summary>The round (brief) or send (entry, item) this approval was requested under: CRM's approval ids
    /// are deterministic per round (<see cref="CrmApprovalIds"/>), so the round is the one whose id matches.
    /// Null for an id CRM did not derive (a legacy row).</summary>
    private static int? RoundOf(ClientApprovalRow row, int maxRound)
    {
        for (int round = maxRound; round >= 1; round--)
        {
            if (CrmApprovalIds.For(row.SubjectType, row.SubjectId, round) == row.Id)
            {
                return round;
            }
        }

        return null;
    }

    private sealed record Subject(Guid ProjectId, Guid? BriefId, Guid? StageId, int? Round, string? Title);

    private sealed record Subjects(IReadOnlyDictionary<Guid, Subject> ById);
}
