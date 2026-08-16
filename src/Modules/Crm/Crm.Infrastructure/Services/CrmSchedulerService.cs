using Crm.Contracts;
using Microsoft.Extensions.Options;

namespace Crm.Infrastructure.Services;

/// <summary>
/// M1-01: ported from <c>../backend/Infrastructure/Services/Crm/CrmSchedulerService.cs</c>, which the
/// M1 task prompt claimed was "already ported" (per <c>PROGRESS.md:4481-4490</c> and two review
/// documents that inherited the claim). It was not — this is the first implementation. Pure
/// computation, no DB, no timer, no <c>IHostedService</c>, plain <c>AddScoped</c> per
/// <c>PLAN-PHASE4.md:129-131</c>.
///
/// <para>Walks the requested window day by day in the office's local time zone. For each day whose
/// <see cref="DayOfWeek"/> is a working day, emits fixed-size back-to-back slots starting at
/// <c>DayStartLocal</c> while the slot still fits before <c>DayEndLocal</c>, keeping only the slots
/// that land fully inside <c>[fromUtc, toUtc]</c>.</para>
///
/// <para>The backend's own uncaught <c>TimeZoneNotFoundException</c> risk — <c>FindSystemTimeZoneById</c>
/// called per request with no try/catch in the handler — is closed one layer up, at
/// <c>CrmModule.Register</c>'s options validation, not here: a misconfigured <c>TimezoneId</c> now fails
/// the app at boot instead of the first free-slots request.</para>
/// </summary>
internal sealed class CrmSchedulerService(IOptions<OfficeHoursOptions> options) : ICrmSchedulerService
{
    public Task<IReadOnlyList<CrmFreeSlot>> GetFreeSlotsAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        if (toUtc <= fromUtc)
            return Task.FromResult<IReadOnlyList<CrmFreeSlot>>([]);

        var o = options.Value;
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(o.TimezoneId);
        var slotSpan = TimeSpan.FromMinutes(o.SlotMinutes);
        var dayStart = TimeSpan.Parse(o.DayStartLocal);
        var dayEnd = TimeSpan.Parse(o.DayEndLocal);

        var fromLocal = TimeZoneInfo.ConvertTimeFromUtc(fromUtc, timeZone);
        var toLocal = TimeZoneInfo.ConvertTimeFromUtc(toUtc, timeZone);

        var slots = new List<CrmFreeSlot>();
        for (var day = fromLocal.Date; day <= toLocal.Date; day = day.AddDays(1))
        {
            if (!o.WorkingDays.Contains((int)day.DayOfWeek))
                continue;

            var slotStart = day + dayStart;
            var dayEndLocal = day + dayEnd;

            while (slotStart + slotSpan <= dayEndLocal)
            {
                var slotEnd = slotStart + slotSpan;
                var slotStartUtc = TimeZoneInfo.ConvertTimeToUtc(slotStart, timeZone);
                var slotEndUtc = TimeZoneInfo.ConvertTimeToUtc(slotEnd, timeZone);

                if (slotStartUtc >= fromUtc && slotEndUtc <= toUtc)
                    slots.Add(new CrmFreeSlot(slotStartUtc, slotEndUtc));

                slotStart = slotEnd;
            }
        }

        return Task.FromResult<IReadOnlyList<CrmFreeSlot>>(slots);
    }
}
