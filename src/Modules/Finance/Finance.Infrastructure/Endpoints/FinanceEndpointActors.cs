using BuildingBlocks;
using Finance.Domain;
using Hr.Contracts;
using SharedKernel;

namespace Finance.Infrastructure.Endpoints;

/// <summary>
/// Six Finance commands stamp a person on the row they create — <c>RecordedById</c> on a ledger entry,
/// <c>GeneratedById</c> on a BOQ snapshot, <c>RequestedById</c>/<c>ApprovedById</c> on a variation,
/// <c>ActorId</c> on an allocation — and every one of them is an <b>HR employee id</b>, not an app user
/// id.
///
/// <para>Resolved here at the boundary, following the S1-01 precedent: "who is calling" is a question
/// the HTTP principal can answer and a handler cannot, and centralising it keeps six otherwise-identical
/// handlers from each repeating the lookup. The backend resolves it inside each handler instead.</para>
///
/// <para><b>This is not decoration: those four columns carry real foreign keys to
/// <c>hr.employees</c></b> (F1-05). A caller who authenticated but has no employee row gets
/// <c>InvalidActor</c> — a 400, not a 403, because it is not an authorization failure: they are a real,
/// permitted user who simply is not an employee, so the command cannot name a person for the row it is
/// about to stamp. Substituting a fabricated Guid would violate the constraint rather than degrade.</para>
/// </summary>
internal static class FinanceEndpointActors
{
    public static async Task<Result<Guid>> ResolveEmployeeIdAsync(
        IHrDirectory directory, ICurrentUser currentUser, CancellationToken ct)
    {
        if (currentUser.UserId is not { } appUserId)
        {
            return FinanceActorErrors.InvalidActor;
        }

        Guid? employeeId = await directory.ResolveEmployeeIdAsync(appUserId, ct);

        return employeeId is { } id ? id : FinanceActorErrors.InvalidActor;
    }
}
