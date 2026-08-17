namespace Notifications.Contracts;

/// <summary>
/// Notification types, grouped in ranges so modules can grow independently: tasks 100–119,
/// stages/gates 120–139, teams 140–149, <b>150–179 RESERVED for Finance</b>, docs/project 180–199,
/// auth 200–209, CRM 500–529.
///
/// <para>Ported from <c>backend/Domain/Enums/NotificationType.cs</c> with the bands and the gaps
/// intact, minus the Finance members — that module is deferred, and its band is held rather than
/// reclaimed so those values mean the same thing when it lands. Port, don't tidy: these values are
/// permanent identifiers on stored rows, and renumbering one silently re-labels history.</para>
///
/// <para>Unlike <c>AuditAction</c>, an unused member here is genuinely inert. There is no
/// "pass a literal and it silently does nothing" path to guard against — a notification type is chosen
/// by the handler that constructs the notification, and a wrong choice is visible in the row.</para>
/// </summary>
public enum NotificationType
{
    // ----- Tasks (100–119) -----
    TaskAssigned = 100,
    TaskStarted = 101,
    TaskApproved = 102,
    TaskRejected = 103,
    TaskCompleted = 104,
    TaskReopened = 105,
    TaskCancelled = 106,
    TaskDueSoon = 107,
    TaskOverdue = 108,

    // ----- Stages & gates (120–139) -----
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

    // ----- Teams (140–149) -----
    TeamMemberAdded = 140,
    TeamMemberRemoved = 141,
    TeamMemberRoleChanged = 142,
    TeamHeadChanged = 143,

    // ----- 150–179 RESERVED for Finance. Do not reclaim. -----

    // ----- Documents & project (180–199) -----
    DocumentUploaded = 180,
    ProjectDueSoon = 181,
    ProjectOverdue = 182,
    ProjectCompleted = 183,

    // ----- Auth (200–209) -----
    UserWelcome = 200,
    PasswordResetRequested = 201,
    AccountActivated = 202,
    AccountDeactivated = 203,

    // ----- CRM (500–529): meetings 500–514, requirement periods 515–529. -----
    // Approvals reuse the generic ApprovalRequested/Approved/Rejected trio (130–132).
    CrmMeetingRequested = 500,
    CrmMeetingProposed = 501,
    CrmMeetingScheduled = 502,
    CrmMeetingDeclined = 503,
    CrmMeetingRescheduled = 504,
    CrmMeetingCanceled = 505,
    CrmMeetingReminder = 506,
    CrmMeetingNoShow = 507,

    // 508 was CrmSlotHoldExpiring, retired with the employee-calendar/hold model. Left unused; do not
    // reuse the value.
    CrmPeriodOpened = 515,
    CrmPeriodExtended = 516,
    CrmPeriodClosed = 517,
}

/// <summary>
/// Deep-link target of a notification, paired with <c>EntityId</c>. <c>Variation</c> and
/// <c>Invoice</c> are held for Finance, same reasoning as <see cref="NotificationType"/>'s reserved
/// band — a stored row's entity type must not change meaning when that module lands.
///
/// <para><c>Site</c> = 15 is the one member the backend does not have: Sites exists here and N1-06
/// notifies on <c>SiteApproved</c>, so it needs a deep-link target. Appended, never inserted.</para>
/// </summary>
public enum NotificationEntityType
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
