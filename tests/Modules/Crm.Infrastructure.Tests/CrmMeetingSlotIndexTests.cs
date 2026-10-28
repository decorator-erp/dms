using Crm.Domain;
using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace Crm.Infrastructure.Tests;

/// <summary>B-14 (MT-N5): the slot table keeps a plain MeetingId index next to the partial unique
/// "one accepted slot" index. Two unnamed <c>HasIndex(s =&gt; s.MeetingId)</c> calls are one index to EF,
/// so the plain one was silently replaced by the partial one and every slots-by-meeting read lost it.</summary>
public class CrmMeetingSlotIndexTests
{
    [Fact]
    public void SlotsHaveAPlainMeetingIdIndex_BesideTheAcceptedSlotIndex()
    {
        using var db = new CrmDbContext(new DbContextOptionsBuilder<CrmDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused").Options);
        IEntityType slot = db.Model.FindEntityType(typeof(CrmMeetingSlot))!;

        IIndex[] onMeetingId = [.. slot.GetIndexes().Where(i => i.Properties.Count == 1 && i.Properties[0].Name == nameof(CrmMeetingSlot.MeetingId))];

        IIndex plain = Assert.Single(onMeetingId, i => i.GetDatabaseName() == "ix_crm_meeting_slots_meeting_id");
        Assert.False(plain.IsUnique);
        Assert.Null(plain.GetFilter());
        Assert.Single(onMeetingId, i => i.GetDatabaseName() == "ux_crm_meeting_slots_meeting_accepted" && i.IsUnique);
    }
}
