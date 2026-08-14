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

/// <summary>Published whenever a <c>Client</c> row is created — by lead conversion today, and by any
/// future direct-create path. Carries <see cref="AppUserId"/> because the only subscriber shape this
/// phase anticipates (none registered yet) would need to resolve the client back to its portal
/// account.</summary>
public sealed record ClientCreatedIntegrationEvent(
    Guid EventId, Guid ClientId, string AppUserId, DateTimeOffset OccurredAtUtc) : IIntegrationEvent;

/// <summary>Published when a <c>CrmMeeting</c> is scheduled from an accepted slot — no subscriber in
/// this repo yet; recorded for the same reason as <see cref="LeadConvertedIntegrationEvent"/>
/// (Notifications' meeting-reminder seam, explicitly deferred per the phase's scope table).</summary>
public sealed record MeetingScheduledIntegrationEvent(
    Guid EventId, Guid MeetingId, Guid ProjectId, DateTime ScheduledAtUtc, DateTimeOffset OccurredAtUtc)
    : IIntegrationEvent;

/// <summary>
/// The three gate-satisfaction events. Each carries <see cref="ProjectStageId"/> so
/// <c>Projects.Infrastructure</c> can match and satisfy the owning gate without a callback — the
/// push half of this phase's push-and-pull gate seam (<c>ICrmGateReader</c> is the pull half).
/// <c>CrmItemsResolved</c> is the only one of the three that can be task-owned (per the backend's
/// <c>CrmItemsResolvedHandler</c> supporting both owner kinds), so only it also carries
/// <see cref="CrmItemsResolvedIntegrationEvent.ProjectTaskId"/>.
/// </summary>
public sealed record CrmBriefApprovedIntegrationEvent(
    Guid EventId, Guid BriefId, Guid ProjectStageId, DateTimeOffset OccurredAtUtc) : IIntegrationEvent;

public sealed record CrmPeriodClosedIntegrationEvent(
    Guid EventId, Guid PeriodId, Guid ProjectStageId, DateTimeOffset OccurredAtUtc) : IIntegrationEvent;

public sealed record CrmItemsResolvedIntegrationEvent(
    Guid EventId, Guid ProjectStageId, Guid? ProjectTaskId, DateTimeOffset OccurredAtUtc)
    : IIntegrationEvent;
