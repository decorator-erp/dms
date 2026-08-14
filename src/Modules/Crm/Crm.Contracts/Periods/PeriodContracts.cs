using BuildingBlocks.Messaging;

namespace Crm.Contracts.Periods;

public sealed record CrmRequirementPeriodResponse(
    Guid Id, Guid ProjectId, Guid? ProjectStageId, RequirementPeriodStatus Status,
    DateTime ClosesAtUtc, DateTime? ClosedAtUtc, Guid OpenedById);

/// <summary>At most one <see cref="RequirementPeriodStatus.Open"/> period per project — the handler
/// enforces the partial-unique rule the backend expressed as a DB index.</summary>
public sealed record OpenCrmRequirementPeriodCommand(
    Guid ProjectId, Guid? ProjectStageId, DateTime ClosesAtUtc, Guid OpenedById) : ICommand<Guid>;

public sealed record ExtendCrmRequirementPeriodCommand(Guid PeriodId, DateTime NewClosesAtUtc) : ICommand;

/// <summary>Closing a period is one of the three CRM gate satisfaction triggers — the handler
/// publishes <c>CrmPeriodClosedIntegrationEvent</c> when <see cref="CrmRequirementPeriodResponse.ProjectStageId"/>
/// is set, in the same <c>SaveChangesAsync</c> as the status change.</summary>
public sealed record CloseCrmRequirementPeriodCommand(Guid PeriodId) : ICommand;

public sealed record GetCrmRequirementPeriodByIdQuery(Guid PeriodId) : IQuery<CrmRequirementPeriodResponse>;

public sealed record GetCrmRequirementPeriodsByProjectQuery(Guid ProjectId)
    : IQuery<IReadOnlyList<CrmRequirementPeriodResponse>>;
