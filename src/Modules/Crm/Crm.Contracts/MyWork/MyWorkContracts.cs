using BuildingBlocks.Messaging;

namespace Crm.Contracts.MyWork;

/// <summary>crm-complete F3 (D5): a task's gathering block. Field names follow the frontend's
/// <c>CrmTaskBlock</c>. Pending entries are drafts awaiting send; pending items await the client's decision;
/// unresolved counts entries and items not yet approved (or not-required).</summary>
public sealed record CrmTaskBlockResponse(
    Guid? BriefId, CrmApprovalState? BriefStatus, int EntryTotal, int EntryPending, int ItemTotal, int ItemPending,
    int UnresolvedTotal, int MeetingsUpcoming);

/// <summary>F3: one open project task assigned to the caller that has gathering work. Follows the frontend's
/// <c>MyGatheringWorkItem</c>.</summary>
public sealed record MyGatheringWorkItemResponse(
    Guid ProjectTaskId, string TaskName, Guid ProjectId, string ProjectName, Guid? ProjectStageId, string? StageName,
    CrmTaskBlockResponse TaskBlock, DateTime? OpenPeriodClosesAtUtc, DateTime? NextMeetingAtUtc);

/// <summary>F3: the calling employee's gathering work across the projects they can reach, at most 200 tasks.</summary>
public sealed record GetMyGatheringWorkQuery(string AppUserId) : IQuery<IReadOnlyList<MyGatheringWorkItemResponse>>, ICrmScoped;
