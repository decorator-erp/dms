using SharedKernel;

namespace Crm.Domain;

/// <summary>
/// Ported from <c>../backend/Domain/Entities/CrmRequirementPeriod.cs</c> — a requirement-gathering
/// window on a project. At most one <see cref="RequirementPeriodStatus.Open"/> period per project is
/// a persistence concern (partial unique index, G9-07). A stage "is a CRM stage" when a period
/// references it — derived, no Projects entity change.
/// </summary>
internal sealed class CrmRequirementPeriod : Entity, IAuditable
{
    public Guid ProjectId { get; private set; }
    public Guid? ProjectStageId { get; private set; }
    public RequirementPeriodStatus Status { get; private set; }
    public DateTime ClosesAtUtc { get; private set; }
    public DateTime? ClosedAtUtc { get; private set; }
    public DateTime? CloseNotifiedAtUtc { get; private set; }
    public Guid OpenedById { get; private set; }

    private CrmRequirementPeriod() { }

    public static Result<CrmRequirementPeriod> Create(
        Guid projectId, Guid? projectStageId, DateTime closesAtUtc, Guid openedById, DateTime? nowUtc = null)
    {
        if (closesAtUtc <= (nowUtc ?? DateTime.UtcNow))
        {
            return CrmPeriodErrors.InvalidCloseTime;
        }

        return new CrmRequirementPeriod
        {
            ProjectId = projectId,
            ProjectStageId = projectStageId,
            Status = RequirementPeriodStatus.Open,
            ClosesAtUtc = closesAtUtc,
            OpenedById = openedById,
        };
    }

    /// <summary>Duration is not fixed — the target close is editable while the period is open.</summary>
    public Result Extend(DateTime newClosesAtUtc, DateTime? nowUtc = null)
    {
        if (Status != RequirementPeriodStatus.Open)
        {
            return CrmPeriodErrors.NotOpen;
        }

        if (newClosesAtUtc <= (nowUtc ?? DateTime.UtcNow))
        {
            return CrmPeriodErrors.InvalidCloseTime;
        }

        ClosesAtUtc = newClosesAtUtc;
        return Result.Success();
    }

    public Result Close(DateTime nowUtc)
    {
        if (Status != RequirementPeriodStatus.Open)
        {
            return CrmPeriodErrors.NotOpen;
        }

        Status = RequirementPeriodStatus.Closed;
        ClosedAtUtc = nowUtc;
        return Result.Success();
    }

    /// <summary>Idempotent — dedupe key already guards the notification.</summary>
    public Result MarkCloseNotified(DateTime nowUtc)
    {
        if (Status != RequirementPeriodStatus.Closed)
        {
            return CrmPeriodErrors.NotOpen;
        }

        if (CloseNotifiedAtUtc is not null)
        {
            return Result.Success();
        }

        CloseNotifiedAtUtc = nowUtc;
        return Result.Success();
    }
}
