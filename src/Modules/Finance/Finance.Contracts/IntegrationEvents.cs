using BuildingBlocks.Events;

namespace Finance.Contracts;

/// <summary>
/// Published when a variation is raised, so Projects can open or reuse a <c>ConditionType.Budget</c>
/// gate on the owner and stamp <c>GateCondition.VariationId</c> (added at F1-00).
///
/// <para>The backend does this with a direct call: <c>VariationDomainService.CreateAsync</c> reaches
/// into the gate repository, finds an unlinked Budget gate or creates one, and stamps the id by
/// <c>ExecuteUpdateAsync</c>. Here that is a cross-module write, so it goes on the outbox — Finance
/// publishes and does not know or care that Projects is what reacts.</para>
///
/// <para>Exactly one of <see cref="ProjectStageId"/>/<see cref="ProjectTaskId"/> identifies the gate
/// owner, matching <c>GateCondition</c>'s own XOR-owner invariant. Consumer: F1-08.</para>
/// </summary>
public sealed record VariationRaisedIntegrationEvent(
    Guid EventId, Guid VariationId, Guid ProjectId, Guid? ProjectStageId, Guid? ProjectTaskId,
    DateTimeOffset OccurredAtUtc) : IIntegrationEvent;

/// <summary>
/// Published when a variation is approved. Projects finds the gate carrying this
/// <see cref="VariationId"/> and satisfies it, running its own unlock cascade.
///
/// <para><see cref="ApprovedById"/> travels so the gate's <c>ResolvedById</c> can be attributed. It is
/// an HR employee id, and <c>gate_conditions."ResolvedById"</c> carries a real FK to
/// <c>hr.employees</c>, so a consumer must not substitute a fabricated Guid when it is absent — null
/// is the only always-valid value there (see <c>GateCondition.MarkSatisfied</c>).</para>
/// </summary>
public sealed record VariationApprovedIntegrationEvent(
    Guid EventId, Guid VariationId, Guid ProjectId, Guid ApprovedById, DateTimeOffset OccurredAtUtc)
    : IIntegrationEvent;

/// <summary>
/// Published when a variation is rejected or cancelled, carrying which in <see cref="Cancelled"/>.
///
/// <para>One event with a discriminator rather than two types, for the reason
/// <c>SiteApprovedIntegrationEvent</c> records: every plausible subscriber cares that "the variation
/// will not proceed" and branches afterwards, and a subscriber wanting only one outcome filtering on a
/// bool is cheaper than one that must register twice to learn the same fact.</para>
///
/// <para>The backend's <c>MarkLinkedGateRejectedAsync</c> deliberately bypasses the gate service's
/// approval-only reject path so the owner stays Locked. F1-08 ports that behaviour on the consumer
/// side.</para>
/// </summary>
public sealed record VariationClosedIntegrationEvent(
    Guid EventId, Guid VariationId, Guid ProjectId, bool Cancelled, DateTimeOffset OccurredAtUtc)
    : IIntegrationEvent;

/// <summary>
/// Published when a task's budget coverage changes — <b>carrying the verdict, not the inputs</b>.
///
/// <para>This is the whole reason Phase 7 ships no <c>IFinanceReader</c>. The backend computes
/// coverage in <c>LedgerEntryDomainService.ResolveTaskBudgetGatesAsync</c> as
/// <c>OverBudgetAmount == 0 || signedClientDifferenceSum &gt;= OverBudgetAmount</c>, and every input
/// to that expression is a Finance column: <c>BudgetLine.AllocatedAmount</c>,
/// <c>AdjustmentAllocation</c>, <c>ActualAmount</c>, and the signed-variation sum. Finance can decide
/// it alone, so it publishes a <see cref="bool"/> and Projects never reads Finance state.</para>
///
/// <para><b><see cref="IsCovered"/> false is not a no-op.</b> The backend re-pends an already-satisfied
/// Budget gate and pushes the owner back to Locked when coverage is lost. A consumer that only handles
/// the true branch silently keeps a gate satisfied on an over-budget task — the de-satisfy path is
/// half of this event's purpose, not an edge case.</para>
/// </summary>
public sealed record TaskBudgetCoverageChangedIntegrationEvent(
    Guid EventId, Guid ProjectTaskId, bool IsCovered, DateTimeOffset OccurredAtUtc) : IIntegrationEvent;

/// <summary>
/// Published when a stage's funding state changes, on the same verdict-not-inputs principle as
/// <see cref="TaskBudgetCoverageChangedIntegrationEvent"/>. The backend's rule
/// (<c>StageFundingSnapshot.IsFunded</c>) is
/// <c>ClientInvoicesSum &gt; 0 &amp;&amp; ClientPaymentsSum &gt;= ClientInvoicesSum</c> — invoices and
/// ledger entries, both Finance-owned.
///
/// <para>Same two-directional contract: <see cref="IsFunded"/> false re-pends satisfied Budget gates on
/// the stage and returns it to Locked.</para>
/// </summary>
public sealed record StageFundingChangedIntegrationEvent(
    Guid EventId, Guid ProjectStageId, bool IsFunded, DateTimeOffset OccurredAtUtc) : IIntegrationEvent;

/// <summary>
/// Published when an invoice is created, which the backend treats as issuance
/// (<c>InvoiceDomainService.CreateAsync</c> raises <c>InvoiceIssuedDomainEvent</c> on create; there is
/// no separate issue step).
///
/// <para><b>No subscriber exists in this repo</b>, and that is stated rather than left to be
/// discovered — the pattern <c>SiteSubmittedIntegrationEvent</c> established after
/// <c>ICrmBriefReader</c> became a dangling seam nobody noticed for two phases. Zero registered
/// handlers is a supported no-op (R2 marks the event processed), not a gap. A client-facing "your
/// invoice is ready" notification is the obvious future consumer; F1-08's <c>InvoiceDueJob</c>
/// deliberately does <b>not</b> use this — it sweeps on due date, not on issuance.</para>
/// </summary>
public sealed record InvoiceIssuedIntegrationEvent(
    Guid EventId, Guid InvoiceId, Guid ProjectId, Guid? ClientId, decimal TotalAmount,
    DateTimeOffset OccurredAtUtc) : IIntegrationEvent;

/// <summary>
/// Published when a ledger entry is posted or reversed. <see cref="Reversal"/> distinguishes the two;
/// <see cref="Amount"/> is always the posted magnitude, never negated, so a subscriber reads the sign
/// from the flag rather than from the number.
///
/// <para><b>No subscriber exists in this repo</b> — same statement-not-omission reasoning as
/// <see cref="InvoiceIssuedIntegrationEvent"/>. The gate consequences of a posting travel on
/// <see cref="TaskBudgetCoverageChangedIntegrationEvent"/>/
/// <see cref="StageFundingChangedIntegrationEvent"/> instead, because those carry a decided verdict and
/// this carries a raw fact; a consumer of this event would have to recompute coverage, which is exactly
/// the read-contract shape Phase 7 avoids.</para>
/// </summary>
public sealed record PaymentPostedIntegrationEvent(
    Guid EventId, Guid LedgerEntryId, Guid ProjectId, decimal Amount, bool Reversal,
    DateTimeOffset OccurredAtUtc) : IIntegrationEvent;
