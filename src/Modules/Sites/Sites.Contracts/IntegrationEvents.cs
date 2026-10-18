using BuildingBlocks.Events;

namespace Sites.Contracts;

/// <summary>
/// Published when a survey is submitted for review, in the same <c>SaveChangesAsync</c> as the status
/// change. IDs and primitives only, per rule 5.
///
/// <para><b>No subscriber exists in this repo.</b> Recorded here — and stated plainly rather than left
/// to be discovered — because a survey reaching review is a fact other modules may plausibly care
/// about (Notifications' reviewer alert, later), and Sites publishes with zero knowledge of who
/// reacts, per <c>docs/adding-a-module.md</c>'s cross-module-write pattern. Zero registered handlers
/// is a supported no-op (the event is marked processed), not a gap — but an UNSTATED zero is how
/// <c>Crm.Contracts.ICrmBriefReader</c> became a dangling seam nobody noticed for two phases (since deleted,
/// with no consumer ever written).</para>
/// </summary>
public sealed record SiteSubmittedIntegrationEvent(
    Guid EventId, Guid SiteId, Guid ProjectId, Guid SubmittedById, DateTimeOffset OccurredAtUtc)
    : IIntegrationEvent;

/// <summary>
/// Published when a submitted survey is decided. <see cref="Approved"/> carries the decision rather
/// than splitting into two event types: every plausible subscriber cares about "a survey was decided"
/// and branches on the outcome, and a subscriber that only wants approvals filtering on one bool is
/// cheaper than one that must register for two events to learn the same fact.
///
/// <para>Consumed by <c>SiteApprovedNotificationHandler</c> since N1-06. This paragraph read "No
/// subscriber exists in this repo" until X-02 found it still saying so two phases after the handler
/// landed — which is why the unconsumed set is now asserted by
/// <c>IntegrationEventConsumerTests</c> rather than carried in prose. A comment cannot notice that it
/// has stopped being true.</para>
/// </summary>
public sealed record SiteApprovedIntegrationEvent(
    Guid EventId,
    Guid SiteId,
    Guid ProjectId,
    bool Approved,
    Guid DecidedById,
    DateTimeOffset OccurredAtUtc) : IIntegrationEvent;
