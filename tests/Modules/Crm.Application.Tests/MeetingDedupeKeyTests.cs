using Crm.Infrastructure.Services;
using Xunit;

namespace Crm.Application.Tests;

/// <summary>D-16 / B-F6: the dedupe key's instant is the full round-trip ("O") value, so two schedules in the
/// same minute are two keys, and the same instant is always the same key.</summary>
public sealed class MeetingDedupeKeyTests
{
    [Fact]
    public void D16_B6_TheInstant_KeepsFullPrecision()
    {
        var at = new DateTime(2026, 11, 2, 9, 30, 0, DateTimeKind.Utc);

        Assert.NotEqual(MeetingRecipients.Instant(at), MeetingRecipients.Instant(at.AddSeconds(20)));
        Assert.NotEqual(MeetingRecipients.Instant(at), MeetingRecipients.Instant(at.AddTicks(1)));
        Assert.Equal(MeetingRecipients.Instant(at), MeetingRecipients.Instant(new DateTime(at.Ticks, DateTimeKind.Utc)));
        Assert.Equal("2026-11-02T09:30:00.0000000Z", MeetingRecipients.Instant(at));
    }
}
