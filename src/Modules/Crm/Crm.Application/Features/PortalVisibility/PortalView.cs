using Crm.Contracts.Portal;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using Documents.Contracts;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using Sites.Contracts;
using DomainKind = Crm.Domain.PortalShareKind;

namespace Crm.Application.Features.PortalVisibility;

/// <summary>A share's item as its owning module has it now. Absent from the map when the source is gone.</summary>
internal sealed record PortalResolvedItem(
    Guid? DocumentId, string? Name, string? ContentType, long FileSizeBytes, int? Version, string? StageName,
    string? SiteCode, TaskDetailSummary? Task);

/// <summary>Everything the client could see on one project, loaded once: the switches, the active shares (display
/// order), their items, the live published updates and the project's stages in order.</summary>
internal sealed record PortalSnapshot(
    Guid ProjectId, CrmPortalSettings? Settings, IReadOnlyList<CrmPortalShare> Shares,
    IReadOnlyDictionary<Guid, PortalResolvedItem> Items, IReadOnlyList<CrmPortalUpdate> Updates,
    IReadOnlyList<StageTimelineSummary> Stages);

/// <summary>
/// P2-A: the ONE code path behind every client portal-visibility route and the office preview, so the preview is the
/// client's view by construction. Item data comes only through the owners' contracts — Documents
/// (<see cref="IDocumentQueries"/>), Projects (<see cref="IProjectDirectory"/>), Sites (<see cref="ISiteDocumentReader"/>)
/// — in a fixed number of batch calls per request, never per share. The rule itself is <see cref="CrmPortalVisibility"/>.
/// </summary>
internal sealed class PortalView(CrmDbContext db, IProjectDirectory projects, IDocumentQueries documents, ISiteDocumentReader sitePhotos)
{
    public static string FileUrl(Guid projectId, Guid shareId) => $"/api/crm/portal/projects/{projectId}/files/{shareId}";

    public async Task<PortalSnapshot> LoadAsync(Guid projectId, CancellationToken ct)
    {
        CrmPortalSettings? settings = await db.CrmPortalSettings.AsNoTracking().FirstOrDefaultAsync(s => s.ProjectId == projectId, ct);
        List<CrmPortalShare> shares = await ActiveSharesQuery(projectId).ToListAsync(ct);
        List<CrmPortalUpdate> updates = await db.CrmPortalUpdates.AsNoTracking()
            .Include(u => u.Attachments)
            .Where(u => u.ProjectId == projectId && u.IsActive && u.PublishedAtUtc != null)
            .OrderByDescending(u => u.PublishedAtUtc).ThenByDescending(u => u.Id)
            .ToListAsync(ct);
        IReadOnlyList<StageTimelineSummary> stages = await projects.GetStageTimelineAsync(projectId, ct);
        IReadOnlyDictionary<Guid, PortalResolvedItem> items = await ResolveAsync(projectId, shares, stages, ct);
        return new PortalSnapshot(projectId, settings, shares, items, updates, stages);
    }

    public IQueryable<CrmPortalShare> ActiveSharesQuery(Guid projectId) =>
        db.CrmPortalShares.AsNoTracking()
            .Where(s => s.ProjectId == projectId && s.IsActive)
            .OrderBy(s => s.SortOrder).ThenBy(s => s.SharedAtUtc).ThenBy(s => s.Id);

    /// <summary>Resolves every share of one project to its item, in batch. A share whose item is gone, or now belongs to
    /// another project, is left out.</summary>
    public async Task<IReadOnlyDictionary<Guid, PortalResolvedItem>> ResolveAsync(
        Guid projectId, IReadOnlyList<CrmPortalShare> shares, IReadOnlyList<StageTimelineSummary>? stages, CancellationToken ct)
    {
        Dictionary<Guid, PortalResolvedItem> items = [];
        if (shares.Count == 0)
        {
            return items;
        }

        // Documents and images: the newest active revision of the lineage when following, else exactly the picked one.
        List<CrmPortalShare> files = [.. shares.Where(s => CrmPortalShare.IsDocumentKind(s.ItemKind))];
        var latestByRoot = (await documents.GetLatestFilesAsync(
                [.. files.Where(s => s.FollowLatest).Select(s => s.RootDocumentId!.Value).Distinct()], ct))
            .ToDictionary(f => f.RootDocumentId);

        List<CrmPortalShare> photoShares = [.. shares.Where(s => s.ItemKind == DomainKind.SitePhoto)];
        var links = (await sitePhotos.GetManyAsync([.. photoShares.Select(s => s.ItemId)], ct))
            .Where(l => l.ProjectId == projectId && l.Kind == SiteDocumentKind.Photo)
            .ToDictionary(l => l.Id);

        HashSet<Guid> pinnedIds = [.. files.Where(s => !s.FollowLatest).Select(s => s.ItemId), .. links.Values.Select(l => l.DocumentId)];
        var byId = (await documents.GetFilesAsync(pinnedIds, ct)).ToDictionary(f => f.Id);

        foreach (CrmPortalShare share in files)
        {
            DocumentFileSummary? file = share.FollowLatest
                ? latestByRoot.GetValueOrDefault(share.RootDocumentId!.Value)
                : byId.GetValueOrDefault(share.ItemId);
            if (file is not null && file.ProjectId == projectId)
            {
                items[share.Id] = new PortalResolvedItem(file.Id, file.FileName, file.ContentType, file.FileSizeBytes, file.Version, null, null, null);
            }
        }

        foreach (CrmPortalShare share in photoShares)
        {
            if (links.TryGetValue(share.ItemId, out SiteDocumentSummary? link)
                && byId.TryGetValue(link.DocumentId, out DocumentFileSummary? file) && file.ProjectId == projectId)
            {
                items[share.Id] = new PortalResolvedItem(
                    file.Id, file.FileName, file.ContentType, file.FileSizeBytes, file.Version, null, link.SiteCode, null);
            }
        }

        List<CrmPortalShare> stageAndTaskShares = [.. shares.Where(s => s.ItemKind is DomainKind.Stage or DomainKind.Task)];
        if (stageAndTaskShares.Count > 0)
        {
            stages ??= await projects.GetStageTimelineAsync(projectId, ct);
            var stageById = stages.ToDictionary(s => s.Id);
            var tasks = (await projects.GetTaskDetailsAsync(
                    [.. stageAndTaskShares.Where(s => s.ItemKind == DomainKind.Task).Select(s => s.ItemId)], ct))
                .Where(t => t.ProjectId == projectId)
                .ToDictionary(t => t.Id);

            foreach (CrmPortalShare share in stageAndTaskShares)
            {
                if (share.ItemKind == DomainKind.Stage && stageById.TryGetValue(share.ItemId, out StageTimelineSummary? stage))
                {
                    items[share.Id] = new PortalResolvedItem(null, stage.Name, null, 0, null, stage.Name, null, null);
                }
                else if (share.ItemKind == DomainKind.Task && tasks.TryGetValue(share.ItemId, out TaskDetailSummary? task))
                {
                    string? stageName = task.ProjectStageId is { } sid && stageById.TryGetValue(sid, out StageTimelineSummary? owner)
                        ? owner.Name
                        : null;
                    items[share.Id] = new PortalResolvedItem(null, task.Name, null, 0, null, stageName, null, task);
                }
            }
        }

        return items;
    }

    // ── the client's view, built from one snapshot ──────────────────────────────────────────────────────────────

    public async Task<PortalOverviewResponse> OverviewAsync(ProjectSummary project, PortalSnapshot s, CancellationToken ct)
    {
        CrmPortalSettings? settings = s.Settings;
        PortalSectionsResponse sections = new(
            CrmPortalVisibility.IsOn(settings, PortalSection.Progress), CrmPortalVisibility.IsOn(settings, PortalSection.Tasks),
            CrmPortalVisibility.IsOn(settings, PortalSection.Documents), CrmPortalVisibility.IsOn(settings, PortalSection.Gallery),
            CrmPortalVisibility.IsOn(settings, PortalSection.SiteSurveys), CrmPortalVisibility.IsOn(settings, PortalSection.Updates));

        PortalProgressResponse? progress = null;
        if (sections.Progress)
        {
            ProjectProgressSummary? summary = await projects.GetProgressAsync([project.Id], ct) is [var first, ..] ? first : null;
            var stageShares = VisibleOfKind(s, DomainKind.Stage).ToDictionary(x => x.ItemId);
            (IReadOnlyList<Guid> shown, bool limited) = CrmPortalVisibility.TimelineStages(
                [.. s.Stages.Select(x => x.Id)], stageShares.Keys);
            var stageById = s.Stages.ToDictionary(x => x.Id);
            progress = new PortalProgressResponse(
                summary?.ProgressPercentage ?? 0m, summary?.ActiveStageName, limited,
                [
                    .. shown.Select(id =>
                    {
                        StageTimelineSummary stage = stageById[id];
                        CrmPortalShare? share = stageShares.GetValueOrDefault(id);
                        return new PortalStageResponse(
                            share?.ClientTitle ?? stage.Name, share?.ClientNote, stage.Order, stage.Status.ToString(),
                            stage.EstimatedStartDate, stage.EstimatedEndDate, stage.ActualStartDate, stage.ActualEndDate,
                            stage.ProgressPercentage);
                    }),
                ]);
        }

        PortalSectionCountsResponse counts = new(
            sections.Documents ? Files(s).Count : null,
            sections.Gallery ? VisibleOfKind(s, DomainKind.Image).Count() : null,
            sections.SiteSurveys ? VisibleOfKind(s, DomainKind.SitePhoto).Count() : null,
            sections.Tasks ? Tasks(s).Count : null,
            sections.Updates ? Updates(s).Count : null);

        return new PortalOverviewResponse(project.Id, project.Name, sections, progress, counts);
    }

    public static IReadOnlyList<PortalFileResponse> Files(PortalSnapshot s) =>
        [.. VisibleOfKind(s, DomainKind.Document).Select(x => ToFile(s, x))];

    /// <summary>Images while Gallery is on and site photos while Site surveys is on, in one display order.</summary>
    public static IReadOnlyList<PortalFileResponse> Gallery(PortalSnapshot s) =>
        [.. s.Shares.Where(x => x.ItemKind is DomainKind.Image or DomainKind.SitePhoto && IsVisible(s, x)).Select(x => ToFile(s, x))];

    public static IReadOnlyList<PortalTaskResponse> Tasks(PortalSnapshot s) =>
    [
        .. VisibleOfKind(s, DomainKind.Task).Select(x =>
        {
            PortalResolvedItem item = s.Items[x.Id];
            TaskDetailSummary task = item.Task!;
            return new PortalTaskResponse(
                x.Id, x.ClientTitle ?? task.Name, x.ClientNote, item.StageName, task.Status.ToString(), task.DueDate,
                task.StartedAtUtc, task.CompletedAtUtc, x.SortOrder);
        }),
    ];

    public static IReadOnlyList<PortalUpdateResponse> Updates(PortalSnapshot s)
    {
        if (!CrmPortalVisibility.IsOn(s.Settings, PortalSection.Updates))
        {
            return [];
        }

        var shares = s.Shares.ToDictionary(x => x.Id);
        return
        [
            .. s.Updates.Where(u => CrmPortalVisibility.CanSeeUpdate(s.Settings, u)).Select(u => new PortalUpdateResponse(
                u.Id, u.Title, u.Body, u.PublishedAtUtc!.Value,
                [
                    .. u.Attachments.OrderBy(a => a.SortOrder)
                        .Select(a => shares.GetValueOrDefault(a.ShareId))
                        .OfType<CrmPortalShare>()
                        .Where(x => s.Items.ContainsKey(x.Id) && s.Items[x.Id].DocumentId is not null
                            && CrmPortalVisibility.CanSeeShare(s.Settings, x, attachedToVisibleUpdate: true))
                        .Select(x => ToFile(s, x)),
                ])),
        ];
    }

    /// <summary>Whether a share is attached to an update the client can see — the second way to a file.</summary>
    public static bool IsAttachedToVisibleUpdate(PortalSnapshot s, Guid shareId) =>
        s.Updates.Any(u => CrmPortalVisibility.CanSeeUpdate(s.Settings, u) && u.Attachments.Any(a => a.ShareId == shareId));

    private static bool IsVisible(PortalSnapshot s, CrmPortalShare share) =>
        s.Items.ContainsKey(share.Id) && CrmPortalVisibility.CanSeeShare(s.Settings, share);

    private static IEnumerable<CrmPortalShare> VisibleOfKind(PortalSnapshot s, DomainKind kind) =>
        s.Shares.Where(x => x.ItemKind == kind && IsVisible(s, x));

    private static PortalFileResponse ToFile(PortalSnapshot s, CrmPortalShare share)
    {
        PortalResolvedItem item = s.Items[share.Id];
        return new PortalFileResponse(
            share.Id, CrmEnumMapping.ToContracts(share.ItemKind), share.ClientTitle ?? item.Name!, share.ClientNote, item.Name!,
            item.ContentType!, item.FileSizeBytes, CrmPortalVisibility.IsImage(item.ContentType), share.SortOrder, share.SharedAtUtc,
            FileUrl(s.ProjectId, share.Id));
    }
}
