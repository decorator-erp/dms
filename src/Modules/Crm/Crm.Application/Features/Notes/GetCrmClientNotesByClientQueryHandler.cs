using BuildingBlocks.Messaging;
using Crm.Contracts.Notes;
using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Crm.Application.Features.Notes;

/// <summary>Internal-only — never routed from a Portal endpoint (G9-14). See
/// <c>CrmClientNote</c>'s own doc comment.</summary>
internal sealed class GetCrmClientNotesByClientQueryHandler(CrmDbContext db)
    : IQueryHandler<GetCrmClientNotesByClientQuery, IReadOnlyList<CrmClientNoteResponse>>
{
    public async Task<Result<IReadOnlyList<CrmClientNoteResponse>>> HandleAsync(
        GetCrmClientNotesByClientQuery query, CancellationToken ct = default)
    {
        List<CrmClientNoteResponse> notes = await db.CrmClientNotes.AsNoTracking()
            .Where(n => n.ClientId == query.ClientId)
            .OrderByDescending(n => n.CreatedAtUtc)
            .Select(n => new CrmClientNoteResponse(n.Id, n.ClientId, n.ProjectId, n.Content, n.WrittenById))
            .ToListAsync(ct);

        return notes;
    }
}
