using BuildingBlocks.Messaging;

namespace Crm.Contracts.Briefs;

/// <summary>All editable head fields in one carrier, mirroring the backend's
/// <c>CrmBriefHeadData</c>. Sent atomically by <see cref="UpdateCrmBriefHeadCommand"/>.
/// <see cref="Sector"/> is optional: omitted on create, it comes from the converted lead's intake (the
/// project's first brief) or the project's most recent brief; omitted on update, the stored value is
/// kept, so a sector can be changed but not cleared.</summary>
public sealed record CrmBriefHeadData(
    UnitType UnitType,
    UnitCondition UnitCondition,
    string? Address = null,
    int? FloorNo = null,
    int? UnitsPerFloor = null,
    decimal? AreaSqm = null,
    BuildingKind? BuildingKind = null,
    bool? IsLicensed = null,
    UnitPosition? Position = null,
    ElevatorStatus? ElevatorStatus = null,
    bool? ReceivesFrequentVisitors = null,
    string? VisitorsNote = null,
    bool? UsedDesignerBefore = null,
    PastExperience? DesignerExperience = null,
    string? DesignerExperienceReason = null,
    HiringReason HiringReasons = HiringReason.None,
    string? PreferredStyle = null,
    string? PreferredColors = null,
    FurnitureStatus? FurnitureStatus = null,
    decimal? ExpectedBudget = null,
    TimelinePressure? TimelinePressure = null,
    string? ArchitecturalRequests = null,
    string? OtherNeeds = null,
    bool? IsRented = null,
    FinishingPurpose? FinishingPurpose = null,
    ProjectSector? Sector = null);

public sealed record CrmDesignBriefResponse(
    Guid Id, Guid ProjectId, Guid ProjectStageId, Guid CreatedById, Guid? LastMeetingId,
    CrmApprovalState Status, CrmBriefHeadData Head);

/// <summary><see cref="IsCurrent"/> is false once another entry in the same list supersedes this one —
/// the ledger's live line is the entries with <see cref="IsCurrent"/> true.</summary>
public sealed record CrmBriefSpaceResponse(
    Guid Id, Guid BriefId, Guid? SpaceId, int? CountOrPieces, string? Requirements,
    Guid? MeetingId, CrmApprovalState ApprovalStatus, Guid? SupersedesSpaceId,
    Guid? ProjectTaskId, Guid CreatedById, bool IsCurrent);

/// <summary>Creates the stage-long brief head. One per (project, stage) — the handler rejects a
/// second create for the same stage, mirroring the backend's unique-project_id rule generalized to
/// unique-per-stage. <see cref="ProjectStageId"/> is required and must belong to
/// <see cref="ProjectId"/>.</summary>
public sealed record CreateCrmDesignBriefCommand(
    Guid ProjectId, Guid CreatedById, Guid ProjectStageId, CrmBriefHeadData Data, string UserId)
    : ICommand<Guid>, ICrmProjectScoped
{
    CrmScopeTarget ICrmProjectScoped.ScopeTarget => CrmScopeTarget.Project(ProjectId);
}

public sealed record UpdateCrmBriefHeadCommand(Guid BriefId, CrmBriefHeadData Data, string UserId, Guid? LastMeetingId = null)
    : ICommand, ICrmProjectScoped
{
    CrmScopeTarget ICrmProjectScoped.ScopeTarget => CrmScopeTarget.Brief(BriefId);
}

/// <summary>Sends the head for client approval — captures a <c>CrmBriefHeadSnapshot</c> round and
/// requests an <c>Approvals.Contracts</c> decision via <c>IApprovalRequester</c>.
/// <see cref="SentByUserId"/> is the office user sending it (filled from <c>ICurrentUser</c> by the
/// endpoint, never by the request body); it becomes the approval's requester, so the decision
/// notification reaches the office and not the client who made the decision. It is also the scope
/// subject.</summary>
public sealed record SendCrmBriefCommand(Guid BriefId, string SentByUserId) : ICommand, ICrmProjectScoped
{
    string ICrmProjectScoped.UserId => SentByUserId;
    CrmScopeTarget ICrmProjectScoped.ScopeTarget => CrmScopeTarget.Brief(BriefId);
}

public sealed record CreateCrmBriefSpaceCommand(
    Guid BriefId, Guid CreatedById, string UserId, Guid? SpaceId, int? CountOrPieces = null,
    string? Requirements = null, Guid? MeetingId = null, Guid? ProjectTaskId = null)
    : ICommand<Guid>, ICrmProjectScoped
{
    CrmScopeTarget ICrmProjectScoped.ScopeTarget => CrmScopeTarget.Brief(BriefId);
}

public sealed record UpdateCrmBriefSpaceCommand(
    Guid EntryId, string UserId, Guid? SpaceId, int? CountOrPieces, string? Requirements,
    Guid? MeetingId, Guid? ProjectTaskId) : ICommand, ICrmProjectScoped
{
    CrmScopeTarget ICrmProjectScoped.ScopeTarget => CrmScopeTarget.BriefSpaceEntry(EntryId);
}

/// <summary>Same requester rule as <see cref="SendCrmBriefCommand.SentByUserId"/>.</summary>
public sealed record SendCrmBriefSpaceCommand(Guid EntryId, string SentByUserId) : ICommand, ICrmProjectScoped
{
    string ICrmProjectScoped.UserId => SentByUserId;
    CrmScopeTarget ICrmProjectScoped.ScopeTarget => CrmScopeTarget.BriefSpaceEntry(EntryId);
}

/// <summary>Ledger correction — only a locked (Approved) entry may be superseded, mirroring
/// <c>CrmBriefSpace.CreateSuperseding</c>; it may be superseded once, and the correction keeps the
/// original's <see cref="SpaceId"/>.</summary>
public sealed record SupersedeCrmBriefSpaceCommand(
    Guid OriginalEntryId, Guid CreatedById, string UserId, Guid? SpaceId, int? CountOrPieces = null,
    string? Requirements = null, Guid? MeetingId = null, Guid? ProjectTaskId = null)
    : ICommand<Guid>, ICrmProjectScoped
{
    CrmScopeTarget ICrmProjectScoped.ScopeTarget => CrmScopeTarget.BriefSpaceEntry(OriginalEntryId);
}

/// <summary>Obsolete: a project has one brief per stage, so "the project's brief" is ambiguous. Kept
/// for the frontend; deterministic — returns the most recently created brief. Prefer
/// <see cref="GetCrmBriefByStageQuery"/>.</summary>
public sealed record GetCrmBriefByProjectQuery(Guid ProjectId, string UserId) : IQuery<CrmDesignBriefResponse>, ICrmProjectScoped
{
    CrmScopeTarget ICrmProjectScoped.ScopeTarget => CrmScopeTarget.Project(ProjectId);
}

public sealed record GetCrmBriefByStageQuery(Guid StageId, string UserId) : IQuery<CrmDesignBriefResponse>, ICrmProjectScoped
{
    CrmScopeTarget ICrmProjectScoped.ScopeTarget => CrmScopeTarget.Stage(StageId);
}

public sealed record GetCrmBriefSpacesByBriefQuery(Guid BriefId, string UserId)
    : IQuery<IReadOnlyList<CrmBriefSpaceResponse>>, ICrmProjectScoped
{
    CrmScopeTarget ICrmProjectScoped.ScopeTarget => CrmScopeTarget.Brief(BriefId);
}

/// <summary>One changed head field. Values are raw strings: an enum as its integer, a bool as
/// <c>"true"</c>/<c>"false"</c>, a number invariant; null means "no value".</summary>
public sealed record BriefFieldChangeResponse(string Field, string? PreviousValue, string? CurrentValue);

/// <summary>What changed in the head between <see cref="PreviousRound"/> and <see cref="Round"/> (FR-029).
/// In round 1 there is no previous round, and every field with a value is a change. The client audience
/// never sees <c>ExpectedBudget</c> or <c>LastMeetingId</c>.</summary>
public sealed record BriefHeadDiffResponse(
    Guid BriefId, int Round, int? PreviousRound, IReadOnlyList<BriefFieldChangeResponse> Changes);

/// <summary>The office audience's round diff — includes <c>ExpectedBudget</c>.</summary>
public sealed record GetCrmBriefRoundDiffQuery(Guid BriefId, int Round, string UserId)
    : IQuery<BriefHeadDiffResponse>, ICrmProjectScoped
{
    CrmScopeTarget ICrmProjectScoped.ScopeTarget => CrmScopeTarget.Brief(BriefId);
}

/// <summary>One sent round of the brief head, in round order. <see cref="SentAtUtc"/> is when the round's
/// snapshot was taken.</summary>
public sealed record CrmBriefRoundResponse(int Round, DateTime SentAtUtc, CrmBriefRoundOutcome Outcome, DateTime? DecidedAtUtc);

/// <summary>The office's list of the brief's rounds, voided ones included.</summary>
public sealed record GetCrmBriefRoundsQuery(Guid BriefId, string UserId)
    : IQuery<IReadOnlyList<CrmBriefRoundResponse>>, ICrmProjectScoped
{
    CrmScopeTarget ICrmProjectScoped.ScopeTarget => CrmScopeTarget.Brief(BriefId);
}
