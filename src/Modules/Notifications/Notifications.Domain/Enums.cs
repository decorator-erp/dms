namespace Notifications.Domain;

// Domain-internal copies of the enums Notifications.Contracts exposes publicly, mirroring
// Sites.Domain.Enums alongside Sites.Contracts.Enums and Crm.Domain.Enums alongside
// Crm.Contracts.Enums. Domain never references Contracts (reference matrix: Domain may reference
// SharedKernel only), so the duplication is structural, not a choice. NotificationEnumParityTests
// asserts both copies stay identical in name AND value, in both directions -- these are persisted as
// ints, so a drift silently re-labels stored rows.
//
// ActivityAction is the Domain copy of BuildingBlocks.Auditing.AuditAction rather than of a Contracts
// enum: AuditAction lives in BuildingBlocks (it has to -- AuditLog does, and Contracts references
// BuildingBlocks), and Domain may not reference that either. Same rule, one layer over.

internal enum NotificationType
{
    // ----- Tasks (100-119) -----
    TaskAssigned = 100,
    TaskStarted = 101,
    TaskApproved = 102,
    TaskRejected = 103,
    TaskCompleted = 104,
    TaskReopened = 105,
    TaskCancelled = 106,
    TaskDueSoon = 107,
    TaskOverdue = 108,

    // ----- Stages & gates (120-139) -----
    StageCompleted = 120,
    StageRejected = 121,
    StageDueSoon = 122,
    StageOverdue = 123,
    GateSatisfied = 124,
    GateRejected = 125,
    ApprovalRequested = 130,
    ApprovalApproved = 131,
    ApprovalRejected = 132,
    ApprovalResponseOverdue = 133,
    ApprovalEscalated = 134,

    // ----- Teams (140-149) -----
    TeamMemberAdded = 140,
    TeamMemberRemoved = 141,
    TeamMemberRoleChanged = 142,
    TeamHeadChanged = 143,

    // ----- Finance (150-179). Range reserved by N1-01; the first two slots filled at F1-08. -----
    InvoiceDueSoon = 150,
    InvoiceOverdue = 151,

    // ----- Documents & project (180-199) -----
    DocumentUploaded = 180,
    ProjectDueSoon = 181,
    ProjectOverdue = 182,
    ProjectCompleted = 183,

    // P1-A: project access grants (mirrors Notifications.Contracts).
    ProjectAccessGranted = 184,
    ProjectAccessRevoked = 185,

    // ----- Auth (200-209) -----
    UserWelcome = 200,
    PasswordResetRequested = 201,
    AccountActivated = 202,
    AccountDeactivated = 203,

    // ----- CRM (500-529) -----
    CrmMeetingRequested = 500,
    CrmMeetingProposed = 501,
    CrmMeetingScheduled = 502,
    CrmMeetingDeclined = 503,
    CrmMeetingRescheduled = 504,
    CrmMeetingCanceled = 505,
    CrmMeetingReminder = 506,
    CrmMeetingNoShow = 507,

    // 508 was CrmSlotHoldExpiring, retired. Left unused; do not reuse the value.
    CrmPeriodOpened = 515,
    CrmPeriodExtended = 516,
    CrmPeriodClosed = 517,

    // ----- Sites (530-539). B-S-L3: a survey decision is not a stage decision. -----
    SiteApproved = 530,
    SiteRejected = 531,
}

internal enum NotificationEntityType
{
    Meeting = 1,
    DesignBrief = 2,
    RequirementItem = 3,
    RequirementPeriod = 4,
    ProjectTask = 5,
    ProjectStage = 6,
    GateCondition = 7,
    Approval = 8,
    Variation = 9,
    Invoice = 10,
    Document = 11,
    Project = 12,
    BriefSpace = 13,
    Lead = 14,
    Site = 15,
}

/// <summary>Domain copy of <c>BuildingBlocks.Auditing.AuditAction</c>. Same three members, same
/// values — an activity row and an audit row describing the same change must not disagree about what
/// kind of change it was.</summary>
internal enum ActivityAction
{
    None = 0,
    Create = 1,
    Update = 2,
    Delete = 3,
}
