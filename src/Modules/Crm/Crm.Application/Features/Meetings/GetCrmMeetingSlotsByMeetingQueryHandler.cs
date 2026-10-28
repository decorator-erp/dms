using BuildingBlocks.Messaging;
using Crm.Contracts.Meetings;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Crm.Application.Features.Meetings;

/// <summary>No backend equivalent — kept for the narrower re-read case (refresh just the slot list
/// after a propose/decline, without refetching the whole meeting), satisfying CLAUDE.md's one-and-many
/// rule alongside <see cref="GetCrmMeetingByIdQuery"/>'s embedded slots.
///
/// <para>Loads entities rather than projecting straight to the response: <c>CrmEnumMapping</c>'s static
/// calls are not EF-translatable inside a <c>Select</c>, so the enum conversion happens in memory via
/// <c>MeetingSupport.ToSlotResponse</c>, same as <c>GetMyPortalBriefQueryHandler</c>'s own
/// workaround.</para>
/// </summary>
internal sealed class GetCrmMeetingSlotsByMeetingQueryHandler(CrmDbContext db)
    : IQueryHandler<GetCrmMeetingSlotsByMeetingQuery, IReadOnlyList<CrmMeetingSlotResponse>>
{
    public async Task<Result<IReadOnlyList<CrmMeetingSlotResponse>>> HandleAsync(
        GetCrmMeetingSlotsByMeetingQuery query, CancellationToken ct = default)
    {
        // B-12 (L4): the slots of a soft-deleted meeting are not found either.
        if (!await db.CrmMeetings.AnyAsync(m => m.Id == query.MeetingId && m.IsActive, ct))
        {
            return Result.Failure<IReadOnlyList<CrmMeetingSlotResponse>>(CrmMeetingErrors.NotFound);
        }

        List<CrmMeetingSlot> slots = await db.CrmMeetingSlots.AsNoTracking()
            .Where(s => s.MeetingId == query.MeetingId)
            .OrderBy(s => s.StartsAtUtc)
            .ToListAsync(ct);

        var items = slots.Select(MeetingSupport.ToSlotResponse).ToList();
        return items;
    }
}
