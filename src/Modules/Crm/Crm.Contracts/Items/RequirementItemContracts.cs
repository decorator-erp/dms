using BuildingBlocks.Messaging;

namespace Crm.Contracts.Items;

public sealed record CrmRequirementItemResponse(
    Guid Id, Guid ProjectId, Guid PeriodId, RequirementCategory Category, Guid? SpaceTypeId,
    string? Room, string Title, string? Description, RequirementPriority Priority,
    RequirementSource? SourceType, Guid? MeetingId, Guid? BriefId, Guid? NoteId,
    CrmApprovalState ApprovalStatus, Guid? SupersedesItemId,
    Guid? CreatedByEmployeeId, Guid? CreatedByContactId, Guid? ProjectTaskId);

public sealed record CreateCrmRequirementItemCommand(
    Guid ProjectId, Guid PeriodId, Guid EmployeeId, RequirementCategory Category, string Title,
    RequirementPriority Priority, Guid? SpaceTypeId = null, string? Room = null,
    string? Description = null, RequirementSource? SourceType = null, Guid? MeetingId = null,
    Guid? BriefId = null, Guid? NoteId = null, Guid? ProjectTaskId = null) : ICommand<Guid>;

/// <summary>Trusted immediately (the client's own words) — created <see cref="CrmApprovalState.NotRequired"/>,
/// mirroring <c>CrmRequirementItem.CreateByClient</c>. The only Portal write in this sub-area.</summary>
public sealed record CreateCrmRequirementItemByClientCommand(
    Guid ProjectId, Guid PeriodId, Guid ContactId, RequirementCategory Category, string Title,
    RequirementPriority Priority, Guid? SpaceTypeId = null, string? Room = null, string? Description = null)
    : ICommand<Guid>;

public sealed record UpdateCrmRequirementItemCommand(
    Guid ItemId, RequirementCategory Category, string Title, RequirementPriority Priority,
    Guid? SpaceTypeId, string? Room, string? Description, RequirementSource? SourceType,
    Guid? MeetingId, Guid? BriefId, Guid? NoteId, Guid? ProjectTaskId) : ICommand;

public sealed record SendCrmRequirementItemCommand(Guid ItemId) : ICommand;

/// <summary>Ledger correction — only a locked (Approved/NotRequired) item may be superseded.</summary>
public sealed record SupersedeCrmRequirementItemCommand(
    Guid OriginalItemId, Guid EmployeeId, RequirementCategory Category, string Title,
    RequirementPriority Priority, Guid? SpaceTypeId = null, string? Room = null,
    string? Description = null, RequirementSource? SourceType = null, Guid? MeetingId = null,
    Guid? BriefId = null, Guid? NoteId = null, Guid? ProjectTaskId = null) : ICommand<Guid>;

public sealed record GetCrmRequirementItemsByPeriodQuery(Guid PeriodId)
    : IQuery<IReadOnlyList<CrmRequirementItemResponse>>;

public sealed record GetCrmRequirementItemsByTaskQuery(Guid ProjectTaskId)
    : IQuery<IReadOnlyList<CrmRequirementItemResponse>>;
