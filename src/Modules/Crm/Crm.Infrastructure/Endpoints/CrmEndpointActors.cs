using Crm.Contracts;
using Crm.Infrastructure.Services;
using SharedKernel;

namespace Crm.Infrastructure.Endpoints;

/// <summary>
/// G9-14: several CRM commands take a pre-resolved actor id (<c>CreatedById</c>/<c>WrittenById</c>/
/// <c>OwnerId</c>/<c>EmployeeId</c> for office writes, <c>ContactId</c> for the Portal's own client
/// write) rather than an <c>AppUserId</c> the handler resolves itself — unlike Projects' own handlers,
/// which resolve <c>ICurrentUser.UserId</c> internally. That shape was set by <c>Crm.Contracts</c> at
/// G9-01, before <see cref="ICrmAccessGuard"/> existed; changing it now would mean re-touching every
/// already-committed G9-09→G9-12 handler for no behavioral gain. Centralizing the resolution here, at
/// the endpoint layer, keeps that boundary where every other ASP.NET-facing "who is calling" read
/// already lives, and reuses <see cref="ICrmAccessGuard"/> (G9-08) exactly as its own doc comment
/// anticipates ("What other modules and the Portal endpoints genuinely need is 'who is this caller'").
/// </summary>
internal static class CrmEndpointActors
{
    public static async Task<Result<Guid>> ResolveEmployeeIdAsync(ICrmAccessGuard guard, string? appUserId, CancellationToken ct)
    {
        if (appUserId is null)
        {
            return Result.Failure<Guid>(CrmAccessErrors.NoStanding);
        }

        Result<CrmActor> actor = await guard.ResolveActorAsync(appUserId, ct);
        if (actor.IsFailure)
        {
            return Result.Failure<Guid>(actor.Error);
        }

        return actor.Value!.Kind == CrmActorKind.Employee
            ? actor.Value.EmployeeId!.Value
            : Result.Failure<Guid>(CrmAccessErrors.NotAnEmployee);
    }

    public static async Task<Result<Guid>> ResolveContactIdAsync(ICrmAccessGuard guard, string? appUserId, CancellationToken ct)
    {
        if (appUserId is null)
        {
            return Result.Failure<Guid>(CrmAccessErrors.NoStanding);
        }

        Result<CrmActor> actor = await guard.ResolveActorAsync(appUserId, ct);
        if (actor.IsFailure)
        {
            return Result.Failure<Guid>(actor.Error);
        }

        return actor.Value!.Kind == CrmActorKind.Client && actor.Value.ContactId is { } contactId
            ? contactId
            : Result.Failure<Guid>(CrmAccessErrors.NotAClient);
    }
}
