using BuildingBlocks.Events;

namespace Crm.Contracts;

/// <summary>
/// Published by lead conversion (<c>ConvertLeadCommand</c>), alongside the row's own updates, in the
/// same <c>SaveChangesAsync</c>. IDs and primitives only, per rule 5. No subscriber exists yet inside
/// this repo — recorded here because a lead converting is a fact other modules may plausibly care
/// about (Notifications, later), and CRM publishes with zero knowledge of who reacts, per
/// <c>docs/adding-a-module.md</c>'s cross-module-write pattern.
/// </summary>
public sealed record LeadConvertedIntegrationEvent(
    Guid EventId, Guid LeadId, Guid ClientId, DateTimeOffset OccurredAtUtc) : IIntegrationEvent;

/// <summary>Published whenever a <c>Client</c> row is created, which is <c>CreateClientCommand</c> and
/// nothing else — lead conversion requires an existing client and does not publish this. Carries
/// <see cref="AppUserId"/> because both its subscribers need it: CRM's own
/// <c>ClientCreatedIntegrationEventHandler</c> activates the portal login the create provisioned, and
/// Notifications' <c>ClientCreatedNotificationHandler</c> welcomes them to it.</summary>
public sealed record ClientCreatedIntegrationEvent(
    Guid EventId, Guid ClientId, string AppUserId, DateTimeOffset OccurredAtUtc) : IIntegrationEvent;

/// <summary>Published when a <c>CrmMeeting</c> is scheduled from an accepted slot — no subscriber in
/// this repo yet; recorded for the same reason as <see cref="LeadConvertedIntegrationEvent"/>
/// (Notifications' meeting-reminder seam, explicitly deferred per the phase's scope table).</summary>
public sealed record MeetingScheduledIntegrationEvent(
    Guid EventId, Guid MeetingId, Guid ProjectId, DateTime ScheduledAtUtc, DateTimeOffset OccurredAtUtc)
    : IIntegrationEvent;

/// <summary>
/// The three gate-satisfaction events. <see cref="CrmBriefApprovedIntegrationEvent"/>/
/// <see cref="CrmPeriodClosedIntegrationEvent"/> carry a required <c>ProjectStageId</c> — both source
/// entities (<c>CrmDesignBrief</c>, <c>CrmRequirementPeriod</c>) are stage-owner-only, matching the
/// backend's handlers (G9-13's own verify: <c>OwnerKind</c>-only validation). <c>CrmItemsResolved</c>
/// is the only one of the three that can be task-owned (the backend's <c>CrmItemsResolvedHandler</c>
/// supports both), so <b>both</b> its id fields are nullable with an implicit XOR — exactly one is
/// set, mirroring <c>GateCondition</c>'s own <c>ProjectStageId</c>/<c>ProjectTaskId</c> scope split —
/// rather than forcing a task-owned resolution to also resolve and carry its owning stage id, which
/// would need a needless <c>Projects.Contracts.IProjectDirectory</c> call CRM's own data (the brief's
/// or period's own <c>ProjectStageId</c>, the entry's own <c>ProjectTaskId</c>) never requires. This
/// is a correction from G9-01's initial shape (a required <c>ProjectStageId</c>), found while wiring
/// the real publisher at G9-13 — see that task's PROGRESS.md entry.
/// </summary>
public sealed record CrmBriefApprovedIntegrationEvent(
    Guid EventId, Guid BriefId, Guid ProjectStageId, DateTimeOffset OccurredAtUtc) : IIntegrationEvent;

public sealed record CrmPeriodClosedIntegrationEvent(
    Guid EventId, Guid PeriodId, Guid ProjectStageId, DateTimeOffset OccurredAtUtc) : IIntegrationEvent;

public sealed record CrmItemsResolvedIntegrationEvent(
    Guid EventId, Guid? ProjectStageId, Guid? ProjectTaskId, DateTimeOffset OccurredAtUtc)
    : IIntegrationEvent;
