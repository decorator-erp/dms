using BuildingBlocks.Messaging;
using Crm.Application.Features.Portal;
using Crm.Contracts;
using Crm.Contracts.Portal;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using Documents.Contracts;
using Projects.Contracts;
using SharedKernel;
using Sites.Contracts;

namespace Crm.Application.Features.PortalVisibility;

/// <summary>
/// P2-A, the client's side. Every handler first proves the project is the caller's own — the same
/// <see cref="PortalClients.ResolveClientIdAsync"/> + <c>project.ClientId</c> check the brief uses — and answers 404
/// for anything else (another client's project, an unknown or deactivated one), then builds its answer through
/// <see cref="PortalView"/>, the code the office preview runs too. A section that is off is a 404 as well: to the
/// client it does not exist.
/// </summary>
internal static class ClientPortalAccess
{
    public static async Task<Result<ProjectSummary>> EnsureOwnProjectAsync(
        ICrmAccessGuard guard, IProjectDirectory projects, string appUserId, Guid projectId, CancellationToken ct)
    {
        Result<Guid> clientId = await PortalClients.ResolveClientIdAsync(guard, appUserId, ct);
        if (clientId.IsFailure)
        {
            return Result.Failure<ProjectSummary>(clientId.Error);
        }

        ProjectSummary? project = await projects.GetProjectAsync(projectId, ct);
        return project is null || !project.IsActive || project.ClientId != clientId.Value
            ? Result.Failure<ProjectSummary>(CrmPortalErrors.NotFound)
            : project;
    }
}

internal sealed class GetMyPortalOverviewQueryHandler(
    CrmDbContext db, ICrmAccessGuard guard, IProjectDirectory projects, IDocumentQueries documents, ISiteDocumentReader sitePhotos)
    : IQueryHandler<GetMyPortalOverviewQuery, PortalOverviewResponse>
{
    public async Task<Result<PortalOverviewResponse>> HandleAsync(GetMyPortalOverviewQuery query, CancellationToken ct = default)
    {
        Result<ProjectSummary> project = await ClientPortalAccess.EnsureOwnProjectAsync(guard, projects, query.AppUserId, query.ProjectId, ct);
        if (project.IsFailure)
        {
            return Result.Failure<PortalOverviewResponse>(project.Error);
        }

        var view = new PortalView(db, projects, documents, sitePhotos);
        return await view.OverviewAsync(project.Value!, await view.LoadAsync(query.ProjectId, ct), ct);
    }
}

internal sealed class GetMyPortalFilesQueryHandler(
    CrmDbContext db, ICrmAccessGuard guard, IProjectDirectory projects, IDocumentQueries documents, ISiteDocumentReader sitePhotos)
    : IQueryHandler<GetMyPortalFilesQuery, IReadOnlyList<PortalFileResponse>>
{
    public async Task<Result<IReadOnlyList<PortalFileResponse>>> HandleAsync(GetMyPortalFilesQuery query, CancellationToken ct = default)
    {
        Result<ProjectSummary> project = await ClientPortalAccess.EnsureOwnProjectAsync(guard, projects, query.AppUserId, query.ProjectId, ct);
        if (project.IsFailure)
        {
            return Result.Failure<IReadOnlyList<PortalFileResponse>>(project.Error);
        }

        PortalSnapshot snapshot = await new PortalView(db, projects, documents, sitePhotos).LoadAsync(query.ProjectId, ct);
        return CrmPortalVisibility.IsOn(snapshot.Settings, PortalSection.Documents)
            ? Result.Success(PortalView.Files(snapshot))
            : Result.Failure<IReadOnlyList<PortalFileResponse>>(CrmPortalErrors.NotFound);
    }
}

internal sealed class GetMyPortalGalleryQueryHandler(
    CrmDbContext db, ICrmAccessGuard guard, IProjectDirectory projects, IDocumentQueries documents, ISiteDocumentReader sitePhotos)
    : IQueryHandler<GetMyPortalGalleryQuery, IReadOnlyList<PortalFileResponse>>
{
    public async Task<Result<IReadOnlyList<PortalFileResponse>>> HandleAsync(GetMyPortalGalleryQuery query, CancellationToken ct = default)
    {
        Result<ProjectSummary> project = await ClientPortalAccess.EnsureOwnProjectAsync(guard, projects, query.AppUserId, query.ProjectId, ct);
        if (project.IsFailure)
        {
            return Result.Failure<IReadOnlyList<PortalFileResponse>>(project.Error);
        }

        PortalSnapshot snapshot = await new PortalView(db, projects, documents, sitePhotos).LoadAsync(query.ProjectId, ct);
        return CrmPortalVisibility.IsOn(snapshot.Settings, PortalSection.Gallery)
            || CrmPortalVisibility.IsOn(snapshot.Settings, PortalSection.SiteSurveys)
            ? Result.Success(PortalView.Gallery(snapshot))
            : Result.Failure<IReadOnlyList<PortalFileResponse>>(CrmPortalErrors.NotFound);
    }
}

internal sealed class GetMyPortalTasksQueryHandler(
    CrmDbContext db, ICrmAccessGuard guard, IProjectDirectory projects, IDocumentQueries documents, ISiteDocumentReader sitePhotos)
    : IQueryHandler<GetMyPortalTasksQuery, IReadOnlyList<PortalTaskResponse>>
{
    public async Task<Result<IReadOnlyList<PortalTaskResponse>>> HandleAsync(GetMyPortalTasksQuery query, CancellationToken ct = default)
    {
        Result<ProjectSummary> project = await ClientPortalAccess.EnsureOwnProjectAsync(guard, projects, query.AppUserId, query.ProjectId, ct);
        if (project.IsFailure)
        {
            return Result.Failure<IReadOnlyList<PortalTaskResponse>>(project.Error);
        }

        PortalSnapshot snapshot = await new PortalView(db, projects, documents, sitePhotos).LoadAsync(query.ProjectId, ct);
        return CrmPortalVisibility.IsOn(snapshot.Settings, PortalSection.Tasks)
            ? Result.Success(PortalView.Tasks(snapshot))
            : Result.Failure<IReadOnlyList<PortalTaskResponse>>(CrmPortalErrors.NotFound);
    }
}

internal sealed class GetMyPortalUpdatesQueryHandler(
    CrmDbContext db, ICrmAccessGuard guard, IProjectDirectory projects, IDocumentQueries documents, ISiteDocumentReader sitePhotos)
    : IQueryHandler<GetMyPortalUpdatesQuery, IReadOnlyList<PortalUpdateResponse>>
{
    public async Task<Result<IReadOnlyList<PortalUpdateResponse>>> HandleAsync(GetMyPortalUpdatesQuery query, CancellationToken ct = default)
    {
        Result<ProjectSummary> project = await ClientPortalAccess.EnsureOwnProjectAsync(guard, projects, query.AppUserId, query.ProjectId, ct);
        if (project.IsFailure)
        {
            return Result.Failure<IReadOnlyList<PortalUpdateResponse>>(project.Error);
        }

        PortalSnapshot snapshot = await new PortalView(db, projects, documents, sitePhotos).LoadAsync(query.ProjectId, ct);
        return CrmPortalVisibility.IsOn(snapshot.Settings, PortalSection.Updates)
            ? Result.Success(PortalView.Updates(snapshot))
            : Result.Failure<IReadOnlyList<PortalUpdateResponse>>(CrmPortalErrors.NotFound);
    }
}

/// <summary>Streams one shared file. Every miss is the same 404: not the caller's project, an unknown or unshared
/// share, a stage or task share, a section that is off (and no visible update attaches it), or a source that is
/// deleted or missing from storage. The client never names a document id.</summary>
internal sealed class GetMyPortalFileContentQueryHandler(
    CrmDbContext db, ICrmAccessGuard guard, IProjectDirectory projects, IDocumentQueries documents, ISiteDocumentReader sitePhotos)
    : IQueryHandler<GetMyPortalFileContentQuery, PortalFileContentResponse>
{
    public async Task<Result<PortalFileContentResponse>> HandleAsync(GetMyPortalFileContentQuery query, CancellationToken ct = default)
    {
        Result<ProjectSummary> project = await ClientPortalAccess.EnsureOwnProjectAsync(guard, projects, query.AppUserId, query.ProjectId, ct);
        if (project.IsFailure)
        {
            return Result.Failure<PortalFileContentResponse>(project.Error);
        }

        PortalSnapshot snapshot = await new PortalView(db, projects, documents, sitePhotos).LoadAsync(query.ProjectId, ct);
        CrmPortalShare? share = snapshot.Shares.FirstOrDefault(s => s.Id == query.ShareId);
        if (share is null
            || !snapshot.Items.TryGetValue(share.Id, out PortalResolvedItem? item) || item.DocumentId is not { } documentId
            || !CrmPortalVisibility.CanSeeShare(snapshot.Settings, share, PortalView.IsAttachedToVisibleUpdate(snapshot, share.Id)))
        {
            return Result.Failure<PortalFileContentResponse>(CrmPortalErrors.NotFound);
        }

        DocumentContent? content = await documents.OpenContentAsync(documentId, ct);
        return content is null
            ? Result.Failure<PortalFileContentResponse>(CrmPortalErrors.NotFound)
            : new PortalFileContentResponse(content.FileName, content.ContentType, CrmPortalVisibility.IsImage(content.ContentType), content.Content);
    }
}
