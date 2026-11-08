using BuildingBlocks.Messaging;
using BuildingBlocks.Outbox;
using Crm.Contracts;
using Crm.Contracts.Portal;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using Crm.Infrastructure.Services;
using Documents.Contracts;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using SharedKernel;
using Sites.Contracts;
using DomainKind = Crm.Domain.PortalShareKind;

namespace Crm.Application.Features.PortalVisibility;

/// <summary>
/// P2-A, the office's side: section switches, shares and updates. Every message is <see cref="ICrmProjectScoped"/>
/// on its project, so <c>CrmProjectScopeDecorator</c> has already answered 404 to a caller who cannot reach the
/// project (route claim 403 → scope 404 → state 409). A share or update id that is not on that project is a 404 too.
/// </summary>
internal static class OfficePortal
{
    public static async Task<Result<Guid>> ResolveEmployeeIdAsync(ICrmAccessGuard guard, string userId, CancellationToken ct)
    {
        Result<CrmActor> actor = await guard.ResolveActorAsync(userId, ct);
        if (actor.IsFailure)
        {
            return Result.Failure<Guid>(actor.Error);
        }

        return actor.Value!.Kind == CrmActorKind.Employee && actor.Value.EmployeeId is { } employeeId
            ? employeeId
            : Result.Failure<Guid>(CrmAccessErrors.NotAnEmployee);
    }

    public static Task<CrmPortalShare?> FindActiveShareAsync(CrmDbContext db, Guid projectId, Guid shareId, CancellationToken ct) =>
        db.CrmPortalShares.FirstOrDefaultAsync(s => s.Id == shareId && s.ProjectId == projectId && s.IsActive, ct);

    public static Task<CrmPortalUpdate?> FindUpdateAsync(CrmDbContext db, Guid projectId, Guid updateId, CancellationToken ct) =>
        db.CrmPortalUpdates.Include(u => u.Attachments)
            .FirstOrDefaultAsync(u => u.Id == updateId && u.ProjectId == projectId && u.IsActive, ct);

    /// <summary>An update attaches only active Document, Image or SitePhoto shares of its own project.</summary>
    public static async Task<Result> CheckAttachmentsAsync(CrmDbContext db, Guid projectId, IReadOnlyList<Guid> shareIds, CancellationToken ct)
    {
        List<Guid> ids = [.. shareIds.Distinct()];
        if (ids.Count == 0)
        {
            return Result.Success();
        }

        int valid = await db.CrmPortalShares.CountAsync(
            s => ids.Contains(s.Id) && s.ProjectId == projectId && s.IsActive
                 && (s.ItemKind == DomainKind.Document || s.ItemKind == DomainKind.Image || s.ItemKind == DomainKind.SitePhoto),
            ct);
        return valid == ids.Count ? Result.Success() : CrmPortalShareErrors.InvalidAttachment;
    }

    public static CrmPortalSettingsResponse ToResponse(Guid projectId, CrmPortalSettings? s) => new(
        projectId, s?.ShowProgress ?? false, s?.ShowTasks ?? false, s?.ShowDocuments ?? false, s?.ShowGallery ?? false,
        s?.ShowSiteSurveys ?? false, s?.ShowUpdates ?? false);

    public static CrmPortalUpdateResponse ToResponse(CrmPortalUpdate u) => new(
        u.Id, u.ProjectId, u.Title, u.Body, u.PublishedAtUtc, u.AuthorId, u.CreatedAtUtc,
        [.. u.Attachments.OrderBy(a => a.SortOrder).Select(a => a.ShareId)]);
}

internal sealed class GetCrmPortalSettingsQueryHandler(CrmDbContext db) : IQueryHandler<GetCrmPortalSettingsQuery, CrmPortalSettingsResponse>
{
    public async Task<Result<CrmPortalSettingsResponse>> HandleAsync(GetCrmPortalSettingsQuery query, CancellationToken ct = default) =>
        OfficePortal.ToResponse(query.ProjectId,
            await db.CrmPortalSettings.AsNoTracking().FirstOrDefaultAsync(s => s.ProjectId == query.ProjectId, ct));
}

internal sealed class UpdateCrmPortalSettingsCommandHandler(CrmDbContext db) : ICommandHandler<UpdateCrmPortalSettingsCommand>
{
    public async Task<Result> HandleAsync(UpdateCrmPortalSettingsCommand command, CancellationToken ct = default)
    {
        CrmPortalSettings? settings = await db.CrmPortalSettings.FirstOrDefaultAsync(s => s.ProjectId == command.ProjectId, ct);
        if (settings is null)
        {
            settings = CrmPortalSettings.Create(command.ProjectId);
            db.CrmPortalSettings.Add(settings);
        }

        settings.Set(command.Progress, command.Tasks, command.Documents, command.Gallery, command.SiteSurveys, command.Updates);
        return await PortalWrites.SaveAsync(db, ct);
    }
}

internal sealed class GetCrmPortalSharesQueryHandler(
    CrmDbContext db, IProjectDirectory projects, IDocumentQueries documents, ISiteDocumentReader sitePhotos)
    : IQueryHandler<GetCrmPortalSharesQuery, IReadOnlyList<CrmPortalShareResponse>>
{
    public async Task<Result<IReadOnlyList<CrmPortalShareResponse>>> HandleAsync(GetCrmPortalSharesQuery query, CancellationToken ct = default)
    {
        var view = new PortalView(db, projects, documents, sitePhotos);
        IQueryable<CrmPortalShare> active = view.ActiveSharesQuery(query.ProjectId);
        if (query.Kind is { } kind)
        {
            DomainKind domainKind = CrmEnumMapping.ToDomain(kind);
            active = active.Where(s => s.ItemKind == domainKind);
        }

        List<CrmPortalShare> shares = await active.ToListAsync(ct);
        IReadOnlyDictionary<Guid, PortalResolvedItem> items = await view.ResolveAsync(query.ProjectId, shares, stages: null, ct);
        return Result.Success<IReadOnlyList<CrmPortalShareResponse>>(
        [
            .. shares.Select(s =>
            {
                PortalResolvedItem? item = items.GetValueOrDefault(s.Id);
                return new CrmPortalShareResponse(
                    s.Id, s.ProjectId, CrmEnumMapping.ToContracts(s.ItemKind), s.ItemId, s.FollowLatest, s.ClientTitle, s.ClientNote,
                    s.SortOrder, s.SharedById, s.SharedAtUtc, item is not null, item?.Name, item?.ContentType,
                    CrmPortalVisibility.IsImage(item?.ContentType), item?.Version,
                    s.ItemKind == DomainKind.Task ? item?.StageName : null, item?.SiteCode);
            }),
        ]);
    }
}

/// <summary>Shares one item after proving, through its owner's contract, that it exists on this project.</summary>
internal sealed class CreateCrmPortalShareCommandHandler(
    CrmDbContext db, ICrmAccessGuard guard, IProjectDirectory projects, IDocumentQueries documents, ISiteDocumentReader sitePhotos)
    : ICommandHandler<CreateCrmPortalShareCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreateCrmPortalShareCommand command, CancellationToken ct = default)
    {
        ProjectSummary? project = await projects.GetProjectAsync(command.ProjectId, ct);
        if (project is null)
        {
            return CrmPortalErrors.NotFound;
        }

        if (!project.IsActive)
        {
            return CrmProjectErrors.Inactive;
        }

        DomainKind kind = CrmEnumMapping.ToDomain(command.ItemKind);
        if (!Enum.IsDefined(kind))
        {
            return CrmPortalShareErrors.InvalidItem;
        }

        Result<Guid?> root = await CheckItemAsync(kind, command.ItemId, command.ProjectId, ct);
        if (root.IsFailure)
        {
            return root.Error;
        }

        if (await db.CrmPortalShares.AnyAsync(
                s => s.ProjectId == command.ProjectId && s.ItemKind == kind && s.ItemId == command.ItemId && s.IsActive, ct))
        {
            return CrmPortalShareErrors.AlreadyShared;
        }

        Result<Guid> sharedBy = await OfficePortal.ResolveEmployeeIdAsync(guard, command.UserId, ct);
        if (sharedBy.IsFailure)
        {
            return sharedBy.Error;
        }

        int sortOrder = command.SortOrder ?? (await db.CrmPortalShares
            .Where(s => s.ProjectId == command.ProjectId && s.IsActive)
            .MaxAsync(s => (int?)s.SortOrder, ct) ?? -1) + 1;

        Result<CrmPortalShare> share = CrmPortalShare.Create(
            command.ProjectId, kind, command.ItemId, root.Value, command.FollowLatest, command.ClientTitle, command.ClientNote,
            sortOrder, sharedBy.Value, DateTime.UtcNow);
        if (share.IsFailure)
        {
            return share.Error;
        }

        db.CrmPortalShares.Add(share.Value!);
        if (command.NotifyClient)
        {
            db.Outbox().Enqueue(new CrmPortalItemSharedIntegrationEvent(
                Guid.NewGuid(), share.Value!.Id, command.ProjectId, DateTimeOffset.UtcNow));
        }

        Result saved = await PortalWrites.SaveAsync(db, ct);
        return saved.IsFailure ? saved.Error : share.Value!.Id;
    }

    /// <summary>The item exists, is live and is on this project; for a document or image, its lineage root.</summary>
    private async Task<Result<Guid?>> CheckItemAsync(DomainKind kind, Guid itemId, Guid projectId, CancellationToken ct)
    {
        switch (kind)
        {
            case DomainKind.Document:
            case DomainKind.Image:
                DocumentFileSummary? file = await documents.GetFileAsync(itemId, ct);
                if (file is null || file.ProjectId != projectId)
                {
                    return Result.Failure<Guid?>(CrmPortalShareErrors.ItemNotFound);
                }

                return kind == DomainKind.Image && !CrmPortalVisibility.IsImage(file.ContentType)
                    ? Result.Failure<Guid?>(CrmPortalShareErrors.NotAnImage)
                    : file.RootDocumentId;

            case DomainKind.SitePhoto:
                SiteDocumentSummary? link = await sitePhotos.GetAsync(itemId, ct);
                if (link is null || link.ProjectId != projectId)
                {
                    return Result.Failure<Guid?>(CrmPortalShareErrors.ItemNotFound);
                }

                return link.Kind == SiteDocumentKind.Photo
                    ? Result.Success<Guid?>(null)
                    : Result.Failure<Guid?>(CrmPortalShareErrors.NotAPhoto);

            case DomainKind.Stage:
                StageSummary? stage = await projects.GetStageAsync(itemId, ct);
                return stage is not null && stage.ProjectId == projectId
                    ? Result.Success<Guid?>(null)
                    : Result.Failure<Guid?>(CrmPortalShareErrors.ItemNotFound);

            case DomainKind.Task:
                TaskSummary? task = await projects.GetTaskAsync(itemId, ct);
                return task is not null && task.ProjectId == projectId
                    ? Result.Success<Guid?>(null)
                    : Result.Failure<Guid?>(CrmPortalShareErrors.ItemNotFound);

            default:
                return Result.Failure<Guid?>(CrmPortalShareErrors.InvalidItem);
        }
    }
}

internal sealed class UpdateCrmPortalShareCommandHandler(CrmDbContext db) : ICommandHandler<UpdateCrmPortalShareCommand>
{
    public async Task<Result> HandleAsync(UpdateCrmPortalShareCommand command, CancellationToken ct = default)
    {
        CrmPortalShare? share = await OfficePortal.FindActiveShareAsync(db, command.ProjectId, command.ShareId, ct);
        if (share is null)
        {
            return CrmPortalShareErrors.ShareNotFound;
        }

        Result edited = share.Edit(command.ClientTitle, command.ClientNote, command.FollowLatest);
        return edited.IsFailure ? edited : await PortalWrites.SaveAsync(db, ct);
    }
}

internal sealed class DeleteCrmPortalShareCommandHandler(CrmDbContext db) : ICommandHandler<DeleteCrmPortalShareCommand>
{
    public async Task<Result> HandleAsync(DeleteCrmPortalShareCommand command, CancellationToken ct = default)
    {
        CrmPortalShare? share = await OfficePortal.FindActiveShareAsync(db, command.ProjectId, command.ShareId, ct);
        if (share is null)
        {
            return CrmPortalShareErrors.ShareNotFound;
        }

        share.Unshare();
        return await PortalWrites.SaveAsync(db, ct);
    }
}

internal sealed class ReorderCrmPortalSharesCommandHandler(CrmDbContext db) : ICommandHandler<ReorderCrmPortalSharesCommand>
{
    public async Task<Result> HandleAsync(ReorderCrmPortalSharesCommand command, CancellationToken ct = default)
    {
        List<Guid> ids = [.. command.ShareIds];
        if (ids.Count == 0 || ids.Distinct().Count() != ids.Count)
        {
            return CrmPortalShareErrors.InvalidOrder;
        }

        Dictionary<Guid, CrmPortalShare> shares = await db.CrmPortalShares
            .Where(s => ids.Contains(s.Id) && s.ProjectId == command.ProjectId && s.IsActive)
            .ToDictionaryAsync(s => s.Id, ct);
        if (shares.Count != ids.Count)
        {
            return CrmPortalShareErrors.InvalidOrder;
        }

        for (int i = 0; i < ids.Count; i++)
        {
            shares[ids[i]].MoveTo(i);
        }

        return await PortalWrites.SaveAsync(db, ct);
    }
}

internal sealed class GetCrmPortalUpdatesQueryHandler(CrmDbContext db) : IQueryHandler<GetCrmPortalUpdatesQuery, IReadOnlyList<CrmPortalUpdateResponse>>
{
    public async Task<Result<IReadOnlyList<CrmPortalUpdateResponse>>> HandleAsync(GetCrmPortalUpdatesQuery query, CancellationToken ct = default)
    {
        List<CrmPortalUpdate> updates = await db.CrmPortalUpdates.AsNoTracking()
            .Include(u => u.Attachments)
            .Where(u => u.ProjectId == query.ProjectId && u.IsActive)
            .OrderByDescending(u => u.PublishedAtUtc ?? u.CreatedAtUtc).ThenByDescending(u => u.Id)
            .ToListAsync(ct);
        return Result.Success<IReadOnlyList<CrmPortalUpdateResponse>>([.. updates.Select(OfficePortal.ToResponse)]);
    }
}

internal sealed class CreateCrmPortalUpdateCommandHandler(CrmDbContext db, ICrmAccessGuard guard, IProjectDirectory projects)
    : ICommandHandler<CreateCrmPortalUpdateCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreateCrmPortalUpdateCommand command, CancellationToken ct = default)
    {
        ProjectSummary? project = await projects.GetProjectAsync(command.ProjectId, ct);
        if (project is null)
        {
            return CrmPortalErrors.NotFound;
        }

        if (!project.IsActive)
        {
            return CrmProjectErrors.Inactive;
        }

        IReadOnlyList<Guid> shareIds = command.ShareIds ?? [];
        Result attachments = await OfficePortal.CheckAttachmentsAsync(db, command.ProjectId, shareIds, ct);
        if (attachments.IsFailure)
        {
            return attachments.Error;
        }

        Result<Guid> author = await OfficePortal.ResolveEmployeeIdAsync(guard, command.UserId, ct);
        if (author.IsFailure)
        {
            return author.Error;
        }

        Result<CrmPortalUpdate> update = CrmPortalUpdate.Create(command.ProjectId, command.Title ?? string.Empty, command.Body ?? string.Empty, author.Value, shareIds);
        if (update.IsFailure)
        {
            return update.Error;
        }

        db.CrmPortalUpdates.Add(update.Value!);
        Result saved = await PortalWrites.SaveAsync(db, ct);
        return saved.IsFailure ? saved.Error : update.Value!.Id;
    }
}

internal sealed class UpdateCrmPortalUpdateCommandHandler(CrmDbContext db) : ICommandHandler<UpdateCrmPortalUpdateCommand>
{
    public async Task<Result> HandleAsync(UpdateCrmPortalUpdateCommand command, CancellationToken ct = default)
    {
        CrmPortalUpdate? update = await OfficePortal.FindUpdateAsync(db, command.ProjectId, command.UpdateId, ct);
        if (update is null)
        {
            return CrmPortalShareErrors.UpdateNotFound;
        }

        IReadOnlyList<Guid> shareIds = command.ShareIds ?? [];
        Result attachments = await OfficePortal.CheckAttachmentsAsync(db, command.ProjectId, shareIds, ct);
        if (attachments.IsFailure)
        {
            return attachments;
        }

        Result edited = update.Edit(command.Title ?? string.Empty, command.Body ?? string.Empty, shareIds);
        return edited.IsFailure ? edited : await PortalWrites.SaveAsync(db, ct);
    }
}

internal sealed class DeleteCrmPortalUpdateCommandHandler(CrmDbContext db) : ICommandHandler<DeleteCrmPortalUpdateCommand>
{
    public async Task<Result> HandleAsync(DeleteCrmPortalUpdateCommand command, CancellationToken ct = default)
    {
        CrmPortalUpdate? update = await OfficePortal.FindUpdateAsync(db, command.ProjectId, command.UpdateId, ct);
        if (update is null)
        {
            return CrmPortalShareErrors.UpdateNotFound;
        }

        update.Delete();
        return await PortalWrites.SaveAsync(db, ct);
    }
}

/// <summary>Publishes a draft; with <c>NotifyClient</c> the client is told through the outbox, once (the handler's
/// dedupe key is per update, so a redelivered event writes no second notification).</summary>
internal sealed class PublishCrmPortalUpdateCommandHandler(CrmDbContext db) : ICommandHandler<PublishCrmPortalUpdateCommand>
{
    public async Task<Result> HandleAsync(PublishCrmPortalUpdateCommand command, CancellationToken ct = default)
    {
        CrmPortalUpdate? update = await OfficePortal.FindUpdateAsync(db, command.ProjectId, command.UpdateId, ct);
        if (update is null)
        {
            return CrmPortalShareErrors.UpdateNotFound;
        }

        Result published = update.Publish(DateTime.UtcNow);
        if (published.IsFailure)
        {
            return published;
        }

        if (command.NotifyClient)
        {
            db.Outbox().Enqueue(new CrmPortalUpdatePublishedIntegrationEvent(
                Guid.NewGuid(), update.Id, command.ProjectId, DateTimeOffset.UtcNow));
        }

        return await PortalWrites.SaveAsync(db, ct);
    }
}

/// <summary>Exactly the client's view: the same <see cref="PortalView"/> calls the client routes make, with a section
/// that is off shown as an empty list instead of the client's 404.</summary>
internal sealed class GetCrmPortalPreviewQueryHandler(
    CrmDbContext db, IProjectDirectory projects, IDocumentQueries documents, ISiteDocumentReader sitePhotos)
    : IQueryHandler<GetCrmPortalPreviewQuery, CrmPortalPreviewResponse>
{
    public async Task<Result<CrmPortalPreviewResponse>> HandleAsync(GetCrmPortalPreviewQuery query, CancellationToken ct = default)
    {
        ProjectSummary? project = await projects.GetProjectAsync(query.ProjectId, ct);
        if (project is null)
        {
            return CrmPortalErrors.NotFound;
        }

        var view = new PortalView(db, projects, documents, sitePhotos);
        PortalSnapshot snapshot = await view.LoadAsync(query.ProjectId, ct);
        CrmPortalSettings? s = snapshot.Settings;
        return new CrmPortalPreviewResponse(
            await view.OverviewAsync(project, snapshot, ct),
            CrmPortalVisibility.IsOn(s, PortalSection.Documents) ? PortalView.Files(snapshot) : [],
            CrmPortalVisibility.IsOn(s, PortalSection.Gallery) || CrmPortalVisibility.IsOn(s, PortalSection.SiteSurveys)
                ? PortalView.Gallery(snapshot)
                : [],
            CrmPortalVisibility.IsOn(s, PortalSection.Tasks) ? PortalView.Tasks(snapshot) : [],
            PortalView.Updates(snapshot));
    }
}
