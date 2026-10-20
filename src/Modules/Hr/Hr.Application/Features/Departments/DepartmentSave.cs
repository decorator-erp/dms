using Hr.Domain.Errors;
using Hr.Infrastructure.Persistence;
using Hr.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SharedKernel;

namespace Hr.Application.Features.Departments;

/// <summary>
/// Aug 3.5: the save behind department create and rename. Both check the name first, but two requests
/// can pass that check together, and the second insert then hits the partial unique index. That is the
/// same refusal the check gives (400 <c>DuplicatedName</c>), not a 500.
/// </summary>
internal static class DepartmentSave
{
    public static async Task<Result> SaveAsync(HrDbContext db, string requestedName, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return Result.Success();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: DepartmentConfiguration.UniqueActiveNameIndex,
        })
        {
            // Return at once: the failed statement aborted the command's transaction.
            return DepartmentErrors.DuplicatedName(requestedName);
        }
    }
}
