using BuildingBlocks.Messaging;

namespace Crm.Contracts.Portal;

// ───────────────────────────── P2-A: what the client sees ─────────────────────────────
// Every client response names shares by their share id, never a document, site-link or task id: the client reaches a
// file only through GET /api/crm/portal/projects/{projectId}/files/{shareId}.

/// <summary>Which portal sections are switched on for the project.</summary>
public sealed record PortalSectionsResponse(
    bool Progress, bool Tasks, bool Documents, bool Gallery, bool SiteSurveys, bool Updates);

/// <summary>How many visible items each switched-on section holds; null for a section that is off.
/// <see cref="Images"/> is the Gallery's images, <see cref="SitePhotos"/> the Site surveys section's photos.</summary>
public sealed record PortalSectionCountsResponse(int? Documents, int? Images, int? SitePhotos, int? Tasks, int? Updates);

/// <summary>One stage on the client's timeline. <see cref="Name"/> is the share's client title when the stage is
/// shared with one, else the stage's own name. <see cref="Status"/> is the Projects stage status name
/// (<c>Pending</c>, <c>Active</c>, <c>Completed</c>, …). Planned dates are the stage's estimated dates.</summary>
public sealed record PortalStageResponse(
    string Name, string? Note, int Order, string Status, DateTime? PlannedStartDate, DateTime? PlannedEndDate,
    DateTime? ActualStartDate, DateTime? ActualEndDate, decimal ProgressPercentage);

/// <summary>The Progress section: the project % (completed over live tasks), its active stage, and the timeline —
/// every stage, or only the shared ones once any stage is shared (<see cref="LimitedToSharedStages"/>).</summary>
public sealed record PortalProgressResponse(
    decimal ProgressPercentage, string? ActiveStageName, bool LimitedToSharedStages, IReadOnlyList<PortalStageResponse> Stages);

/// <summary>The client's overview of one of its projects. <see cref="Progress"/> is null while Progress is off.</summary>
public sealed record PortalOverviewResponse(
    Guid ProjectId, string ProjectName, PortalSectionsResponse Sections, PortalProgressResponse? Progress,
    PortalSectionCountsResponse Counts);

/// <summary>A shared file (document, image or site photo) as the client sees it. <see cref="Title"/> is the client
/// title, else the file name. <see cref="Url"/> is the client stream route; images stream inline, everything else as
/// an attachment.</summary>
public sealed record PortalFileResponse(
    Guid ShareId, PortalShareKind Kind, string Title, string? Note, string FileName, string ContentType,
    long FileSizeBytes, bool IsImage, int SortOrder, DateTime SharedAtUtc, string Url);

/// <summary>A shared task. <see cref="Status"/> is the Projects task status name (<c>Pending</c>, <c>InProgress</c>,
/// <c>Completed</c>, …).</summary>
public sealed record PortalTaskResponse(
    Guid ShareId, string Title, string? Note, string? StageName, string Status, DateTime? DueDate,
    DateTime? StartedAtUtc, DateTime? CompletedAtUtc, int SortOrder);

/// <summary>A published update, newest first, with its attachments that are still shared and still exist.</summary>
public sealed record PortalUpdateResponse(
    Guid Id, string Title, string Body, DateTime PublishedAtUtc, IReadOnlyList<PortalFileResponse> Attachments);

/// <summary>An opened shared file. <see cref="Inline"/> for images; the endpoint disposes <see cref="Content"/>.</summary>
public sealed record PortalFileContentResponse(string FileName, string ContentType, bool Inline, Stream Content);

/// <summary>The calling client's overview of its own project: 404 for another client's project or an unknown one.</summary>
public sealed record GetMyPortalOverviewQuery(string AppUserId, Guid ProjectId) : IQuery<PortalOverviewResponse>, ICrmScoped;

/// <summary>Shared documents; 404 while Documents is off.</summary>
public sealed record GetMyPortalFilesQuery(string AppUserId, Guid ProjectId) : IQuery<IReadOnlyList<PortalFileResponse>>, ICrmScoped;

/// <summary>Shared images (while Gallery is on) and site photos (while Site surveys is on); 404 while both are off.</summary>
public sealed record GetMyPortalGalleryQuery(string AppUserId, Guid ProjectId) : IQuery<IReadOnlyList<PortalFileResponse>>, ICrmScoped;

/// <summary>Shared tasks; 404 while Tasks is off.</summary>
public sealed record GetMyPortalTasksQuery(string AppUserId, Guid ProjectId) : IQuery<IReadOnlyList<PortalTaskResponse>>, ICrmScoped;

/// <summary>Published updates, newest first; 404 while Updates is off.</summary>
public sealed record GetMyPortalUpdatesQuery(string AppUserId, Guid ProjectId) : IQuery<IReadOnlyList<PortalUpdateResponse>>, ICrmScoped;

/// <summary>Opens one shared file. 404 when the project is not the caller's, the share is unknown, unshared or not a
/// file, its section is off (and it is not attached to a visible update), or the source is gone.</summary>
public sealed record GetMyPortalFileContentQuery(string AppUserId, Guid ProjectId, Guid ShareId)
    : IQuery<PortalFileContentResponse>, ICrmScoped;

// ───────────────────────────── P2-A: what the office manages ─────────────────────────────
// Every message below is ICrmProjectScoped on its project (a project the caller cannot reach is a 404), and a share or
// update id that is not on that project is a 404 too.

public sealed record CrmPortalSettingsResponse(
    Guid ProjectId, bool Progress, bool Tasks, bool Documents, bool Gallery, bool SiteSurveys, bool Updates);

/// <summary>One share as the office sees it, with the item's current metadata. <see cref="IsAvailable"/> is false when
/// the source no longer resolves (deleted document, unlinked photo, deleted stage or task): the client does not see
/// it. <see cref="ItemName"/> is the file name (the newest revision's when following), or the stage or task name.
/// <see cref="IsImage"/> says a thumbnail can be shown. <see cref="StageName"/> is a task's stage;
/// <see cref="SiteCode"/> a site photo's survey.</summary>
public sealed record CrmPortalShareResponse(
    Guid Id, Guid ProjectId, PortalShareKind ItemKind, Guid ItemId, bool FollowLatest, string? ClientTitle,
    string? ClientNote, int SortOrder, Guid SharedById, DateTime SharedAtUtc, bool IsAvailable, string? ItemName,
    string? ContentType, bool IsImage, int? Version, string? StageName, string? SiteCode);

public sealed record CrmPortalUpdateResponse(
    Guid Id, Guid ProjectId, string Title, string Body, DateTime? PublishedAtUtc, Guid AuthorId, DateTime CreatedAtUtc,
    IReadOnlyList<Guid> ShareIds);

/// <summary>Exactly what the client would see, built by the client routes' own code: the overview and every section
/// list (an empty list for a section that is off).</summary>
public sealed record CrmPortalPreviewResponse(
    PortalOverviewResponse Overview, IReadOnlyList<PortalFileResponse> Files, IReadOnlyList<PortalFileResponse> Gallery,
    IReadOnlyList<PortalTaskResponse> Tasks, IReadOnlyList<PortalUpdateResponse> Updates);

public sealed record GetCrmPortalSettingsQuery(Guid ProjectId, string UserId) : IQuery<CrmPortalSettingsResponse>, ICrmProjectScoped
{
    CrmScopeTarget ICrmProjectScoped.ScopeTarget => CrmScopeTarget.Project(ProjectId);
}

/// <summary>Replaces all six switches.</summary>
public sealed record UpdateCrmPortalSettingsCommand(
    Guid ProjectId, bool Progress, bool Tasks, bool Documents, bool Gallery, bool SiteSurveys, bool Updates, string UserId)
    : ICommand, ICrmProjectScoped
{
    CrmScopeTarget ICrmProjectScoped.ScopeTarget => CrmScopeTarget.Project(ProjectId);
}

/// <summary>The project's active shares in display order, optionally of one kind.</summary>
public sealed record GetCrmPortalSharesQuery(Guid ProjectId, string UserId, PortalShareKind? Kind = null)
    : IQuery<IReadOnlyList<CrmPortalShareResponse>>, ICrmProjectScoped
{
    CrmScopeTarget ICrmProjectScoped.ScopeTarget => CrmScopeTarget.Project(ProjectId);
}

/// <summary>Shares one item. The item must exist on this project (404 <c>crm.portal.ItemNotFound</c>); an Image must
/// be <c>image/*</c> and a SitePhoto a Photo link (400); a second active share of the same item is 409. Without
/// <see cref="SortOrder"/> the share goes last. <see cref="NotifyClient"/> sends the client one
/// <c>CrmPortalItemShared</c> notification once the share is visible to them.</summary>
public sealed record CreateCrmPortalShareCommand(
    Guid ProjectId, PortalShareKind ItemKind, Guid ItemId, string? ClientTitle, string? ClientNote, bool FollowLatest,
    int? SortOrder, bool NotifyClient, string UserId) : ICommand<Guid>, ICrmProjectScoped
{
    CrmScopeTarget ICrmProjectScoped.ScopeTarget => CrmScopeTarget.Project(ProjectId);
}

public sealed record UpdateCrmPortalShareCommand(
    Guid ProjectId, Guid ShareId, string? ClientTitle, string? ClientNote, bool FollowLatest, string UserId)
    : ICommand, ICrmProjectScoped
{
    CrmScopeTarget ICrmProjectScoped.ScopeTarget => CrmScopeTarget.Project(ProjectId);
}

/// <summary>Unshare: a soft delete, effective on the client's next request.</summary>
public sealed record DeleteCrmPortalShareCommand(Guid ProjectId, Guid ShareId, string UserId) : ICommand, ICrmProjectScoped
{
    CrmScopeTarget ICrmProjectScoped.ScopeTarget => CrmScopeTarget.Project(ProjectId);
}

/// <summary>Sets the listed shares' sort order to their position in the list. Each must be an active share of the
/// project, listed once (else 400); shares not listed keep their order.</summary>
public sealed record ReorderCrmPortalSharesCommand(Guid ProjectId, IReadOnlyList<Guid> ShareIds, string UserId)
    : ICommand, ICrmProjectScoped
{
    CrmScopeTarget ICrmProjectScoped.ScopeTarget => CrmScopeTarget.Project(ProjectId);
}

/// <summary>Every live update of the project, drafts included, newest first.</summary>
public sealed record GetCrmPortalUpdatesQuery(Guid ProjectId, string UserId)
    : IQuery<IReadOnlyList<CrmPortalUpdateResponse>>, ICrmProjectScoped
{
    CrmScopeTarget ICrmProjectScoped.ScopeTarget => CrmScopeTarget.Project(ProjectId);
}

/// <summary>A draft update. <see cref="ShareIds"/> must be active Document, Image or SitePhoto shares of the project.</summary>
public sealed record CreateCrmPortalUpdateCommand(
    Guid ProjectId, string Title, string? Body, IReadOnlyList<Guid> ShareIds, string UserId) : ICommand<Guid>, ICrmProjectScoped
{
    CrmScopeTarget ICrmProjectScoped.ScopeTarget => CrmScopeTarget.Project(ProjectId);
}

public sealed record UpdateCrmPortalUpdateCommand(
    Guid ProjectId, Guid UpdateId, string Title, string? Body, IReadOnlyList<Guid> ShareIds, string UserId)
    : ICommand, ICrmProjectScoped
{
    CrmScopeTarget ICrmProjectScoped.ScopeTarget => CrmScopeTarget.Project(ProjectId);
}

public sealed record DeleteCrmPortalUpdateCommand(Guid ProjectId, Guid UpdateId, string UserId) : ICommand, ICrmProjectScoped
{
    CrmScopeTarget ICrmProjectScoped.ScopeTarget => CrmScopeTarget.Project(ProjectId);
}

/// <summary>Publishes a draft (409 <c>crm.portal.AlreadyPublished</c> when already published). With
/// <see cref="NotifyClient"/> the client gets one <c>CrmPortalUpdatePosted</c> notification.</summary>
public sealed record PublishCrmPortalUpdateCommand(Guid ProjectId, Guid UpdateId, bool NotifyClient, string UserId)
    : ICommand, ICrmProjectScoped
{
    CrmScopeTarget ICrmProjectScoped.ScopeTarget => CrmScopeTarget.Project(ProjectId);
}

public sealed record GetCrmPortalPreviewQuery(Guid ProjectId, string UserId) : IQuery<CrmPortalPreviewResponse>, ICrmProjectScoped
{
    CrmScopeTarget ICrmProjectScoped.ScopeTarget => CrmScopeTarget.Project(ProjectId);
}
