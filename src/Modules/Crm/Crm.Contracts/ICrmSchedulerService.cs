namespace Crm.Contracts;

public sealed record CrmFreeSlot(DateTime StartsAtUtc, DateTime EndsAtUtc);

/// <summary>
/// M1-01: office-hours-driven candidate meeting times. Pure computation over
/// <c>Crm.Infrastructure.Services.OfficeHoursOptions</c> - no persistence, no per-project or
/// per-employee calendar, no <c>IHostedService</c>. Public on <c>Crm.Contracts</c>, matching
/// <see cref="ICrmAccessGuard"/>'s own split: the interface is the public seam <c>Crm.Application</c>
/// depends on, while the implementation (<c>Crm.Infrastructure.Services.CrmSchedulerService</c>) stays
/// <c>internal</c>.
/// </summary>
public interface ICrmSchedulerService
{
    Task<IReadOnlyList<CrmFreeSlot>> GetFreeSlotsAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct);
}
