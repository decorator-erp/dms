using Crm.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SharedKernel;

namespace Crm.Infrastructure.Persistence;

/// <summary>
/// Save for artifact links — <see cref="CatalogWrites"/>' shape (L-4). A link of a document its subject
/// already has hits one of the four partial unique indexes (one per subject column) and maps to
/// <see cref="CrmArtifactErrors.AlreadyLinked"/>, a 409, instead of a 500. Anything else still throws.
/// </summary>
internal static class ArtifactWrites
{
    public const string BriefIndex = "ux_crm_artifact_documents_document_brief";
    public const string SpaceIndex = "ux_crm_artifact_documents_document_space";
    public const string RequirementItemIndex = "ux_crm_artifact_documents_document_requirement_item";
    public const string MeetingIndex = "ux_crm_artifact_documents_document_meeting";

    private static readonly HashSet<string> LinkIndexes = new(StringComparer.Ordinal)
    {
        BriefIndex, SpaceIndex, RequirementItemIndex, MeetingIndex,
    };

    public static async Task<Result> SaveAsync(CrmDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return Result.Success();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: { } name,
        } && LinkIndexes.Contains(name))
        {
            return CrmArtifactErrors.AlreadyLinked;
        }
    }
}
