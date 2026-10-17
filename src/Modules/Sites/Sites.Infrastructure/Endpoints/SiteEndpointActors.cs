using BuildingBlocks;
using Hr.Contracts;
using SharedKernel;
using Sites.Domain;

namespace Sites.Infrastructure.Endpoints;

/// <summary>
/// Three Sites commands take a pre-resolved HR employee id — <c>CreatedById</c>, <c>DecidedById</c>,
/// <c>AddedById</c> — rather than an app user id the handler resolves itself.
///
/// <para>That shape was chosen at S1-01 following CRM's G9-14 precedent: "who is calling" questions
/// belong at the boundary where the HTTP principal already lives, and centralizing them here keeps
/// three otherwise-identical handlers from each repeating the same lookup. The backend does the
/// opposite — every one of those three handlers resolves the employee itself.</para>
///
/// <para>A caller who authenticated but has no HR employee row gets <c>InvalidData</c>, matching the
/// backend's own answer for that case. It is a 400 rather than a 403 because it is not an authorization
/// failure: they are a real, permitted user who simply is not an employee, so the command cannot name a
/// person for the row it is about to stamp.</para>
/// </summary>
internal static class SiteEndpointActors
{
    public static async Task<Result<Guid>> ResolveEmployeeIdAsync(
        IHrDirectory directory, ICurrentUser currentUser, CancellationToken ct)
    {
        if (currentUser.UserId is not { } appUserId)
        {
            return SiteErrors.InvalidData;
        }

        Guid? employeeId = await directory.ResolveEmployeeIdAsync(appUserId, ct);

        return employeeId is { } id ? id : SiteErrors.InvalidData;
    }
}
