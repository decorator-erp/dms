using Documents.Domain;
using Documents.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using SharedKernel;

namespace Documents.Application.Features.Gates;

/// <summary>
/// A gate that waits on the referenced document lineage being approved. Validation here only checks the
/// lineage exists and belongs to the same project — reactive satisfaction (does the LATEST version have
/// an approved review) happens later, when <c>ApprovalDecidedIntegrationEventHandler</c> (this module)
/// reacts to a <c>ProjectDocument</c>-subject decision.
///
/// Reads Documents' OWN <see cref="DocumentsDbContext"/> directly — the backend's equivalent handler
/// goes through <c>ICrmGateReader</c> indirection (a cross-cutting reader interface spanning CRM/Documents
/// concerns in the old monolith); that indirection is deliberately NOT ported (per the plan's
/// pre-resolved facts) since this module has direct access to its own data and needs no intermediary.
/// </summary>
internal sealed class DocumentApprovedHandler(DocumentsDbContext db) : IGateConditionTypeHandler
{
    public ConditionType Type => ConditionType.DocumentApproved;

    public async Task<Result> ValidateAsync(GateConditionContext context, CancellationToken ct = default)
    {
        if (context.ReferencedDocumentId is not { } rootDocumentId)
        {
            return GateReferenceErrors.InvalidReference;
        }

        Guid? referencedProjectId = await db.ProjectDocuments.AsNoTracking()
            .Where(d => d.RootDocumentId == rootDocumentId)
            .Select(d => (Guid?)d.ProjectId)
            .FirstOrDefaultAsync(ct);

        if (referencedProjectId is null)
        {
            return GateReferenceErrors.DocumentNotFound;
        }

        if (referencedProjectId != context.OwnerProjectId)
        {
            return GateReferenceErrors.CrossProjectReference;
        }

        return Result.Success();
    }
}
