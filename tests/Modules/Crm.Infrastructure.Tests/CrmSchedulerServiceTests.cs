using Crm.Infrastructure.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace Crm.Infrastructure.Tests;

/// <summary>
/// M1-01: <see cref="CrmSchedulerService"/> is pure computation — no DB, no container, unlike this
/// project's other tests. Uses a fixed <see cref="OfficeHoursOptions"/> instance rather than the
/// section-bound defaults, so the assertions don't silently drift if the defaults ever change.
/// </summary>
public sealed class CrmSchedulerServiceTests
{
    private static readonly OfficeHoursOptions Options = new()
    {
        WorkingDays = [1, 2, 3, 4, 5], // Mon-Fri
        DayStartLocal = "09:00",
        DayEndLocal = "11:00",
        SlotMinutes = 60,
        TimezoneId = "UTC",
    };

    private static CrmSchedulerService CreateService(OfficeHoursOptions? options = null) =>
        new(Microsoft.Extensions.Options.Options.Create(options ?? Options));

    [Fact]
    public async Task GetFreeSlotsAsync_ReturnsBackToBackSlotsWithinWorkingHours()
    {
        // Monday 2026-01-05 00:00 UTC through end of day - one working day, 09:00-11:00, 60-minute slots.
        var service = CreateService();

        var slots = await service.GetFreeSlotsAsync(
            new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 6, 0, 0, 0, DateTimeKind.Utc),
            CancellationToken.None);

        Assert.Equal(2, slots.Count);
        Assert.Equal(new DateTime(2026, 1, 5, 9, 0, 0, DateTimeKind.Utc), slots[0].StartsAtUtc);
        Assert.Equal(new DateTime(2026, 1, 5, 10, 0, 0, DateTimeKind.Utc), slots[0].EndsAtUtc);
        Assert.Equal(new DateTime(2026, 1, 5, 10, 0, 0, DateTimeKind.Utc), slots[1].StartsAtUtc);
        Assert.Equal(new DateTime(2026, 1, 5, 11, 0, 0, DateTimeKind.Utc), slots[1].EndsAtUtc);
    }

    [Fact]
    public async Task GetFreeSlotsAsync_SkipsNonWorkingDays()
    {
        // 2026-01-03/04 is a Saturday/Sunday - neither is in the Mon-Fri WorkingDays list.
        var service = CreateService();

        var slots = await service.GetFreeSlotsAsync(
            new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc),
            CancellationToken.None);

        Assert.Empty(slots);
    }

    [Fact]
    public async Task GetFreeSlotsAsync_ExcludesSlotsOutsideTheRequestedWindow()
    {
        // Window ends at 10:30 - the 10:00-11:00 slot no longer fits fully inside [from, to).
        var service = CreateService();

        var slots = await service.GetFreeSlotsAsync(
            new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 5, 10, 30, 0, DateTimeKind.Utc),
            CancellationToken.None);

        var slot = Assert.Single(slots);
        Assert.Equal(new DateTime(2026, 1, 5, 9, 0, 0, DateTimeKind.Utc), slot.StartsAtUtc);
    }

    [Fact]
    public async Task GetFreeSlotsAsync_WhenToBeforeFrom_ReturnsEmpty()
    {
        var service = CreateService();

        var slots = await service.GetFreeSlotsAsync(
            new DateTime(2026, 1, 5, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 5, 9, 0, 0, DateTimeKind.Utc),
            CancellationToken.None);

        Assert.Empty(slots);
    }

    [Fact]
    public async Task GetFreeSlotsAsync_WithBadTimezoneId_Throws()
    {
        var badOptions = new OfficeHoursOptions
        {
            WorkingDays = Options.WorkingDays,
            DayStartLocal = Options.DayStartLocal,
            DayEndLocal = Options.DayEndLocal,
            SlotMinutes = Options.SlotMinutes,
            TimezoneId = "Not/A_Real_Zone",
        };
        var service = CreateService(badOptions);

        await Assert.ThrowsAsync<TimeZoneNotFoundException>(() => service.GetFreeSlotsAsync(
            new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 6, 0, 0, 0, DateTimeKind.Utc),
            CancellationToken.None));
    }
}
