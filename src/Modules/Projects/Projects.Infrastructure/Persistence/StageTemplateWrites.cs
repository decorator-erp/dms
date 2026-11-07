using Microsoft.EntityFrameworkCore;
using Npgsql;
using Projects.Domain;
using Projects.Infrastructure.Persistence.Configurations;
using SharedKernel;

namespace Projects.Infrastructure.Persistence;

/// <summary>Saves a stage template create or rename and turns a name another write took first
/// (<c>IX_stage_templates_Name</c>) into 409 <see cref="StageTemplateErrors.NameTaken"/> instead of a 500, as
/// <see cref="ProjectWrites"/> does for the project code. Anything else still throws.</summary>
internal static class StageTemplateWrites
{
    public static async Task<Result> SaveAsync(ProjectsDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return Result.Success();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: StageTemplateConfiguration.NameIndex,
        })
        {
            return StageTemplateErrors.NameTaken;
        }
    }
}
