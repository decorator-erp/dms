using SharedKernel;

namespace Crm.Contracts;

/// <summary>
/// The resolved principal behind a CRM request. Exactly one identity is set:
/// <see cref="EmployeeId"/> for <see cref="CrmActorKind.Employee"/>, or
/// <see cref="ContactId"/>/<see cref="ClientId"/> for <see cref="CrmActorKind.Client"/>.
/// <see cref="HasReadAll"/> is the single override — the backend's <c>CrmAccessService</c> has no
/// role or admin special-casing beyond it, and neither does this port.
/// </summary>
public sealed record CrmActor(
    CrmActorKind Kind, Guid? EmployeeId, Guid? ContactId, Guid? ClientId, bool HasReadAll);

/// <summary>
/// Row-level access guard, ported from the backend's <c>ICrmAccessService</c>. Mirrors
/// <c>Projects.Contracts.IProjectAccessGuard</c>: read-all bypass, 404 for missing, 403 for
/// exists-but-unrelated — in that order, matching <c>CLAUDE.md</c>'s authorization order (route claims
/// → module scope decorator → aggregate state).
///
/// <para><b>Only the resolution half is on this contract.</b> The backend's fourteen
/// <c>EnsureLeadAsync</c>/<c>EnsureBriefAsync</c>/… methods each query CRM's own tables and are used
/// only from inside CRM, so they stay on an internal service in <c>Crm.Application</c> rather than
/// widening the public surface. What other modules and the Portal endpoints genuinely need is "who is
/// this caller, in CRM's terms" — that is what lives here.</para>
///
/// <para>The employee branch resolves through <c>Hr.Contracts.IHrDirectory.ResolveEmployeeIdAsync</c>;
/// the client branch is CRM-owned data and stays local. That split is why this is depth-1: CRM asks HR
/// one question, HR asks nobody.</para>
/// </summary>
public interface ICrmAccessGuard
{
    /// <summary>Resolves <paramref name="appUserId"/> to an employee or a client actor. Fails with a
    /// 403-shaped error when the user is neither — an authenticated principal with no CRM identity is
    /// not an error of authentication, it is a caller with no standing here.</summary>
    Task<Result<CrmActor>> ResolveActorAsync(string appUserId, CancellationToken ct);

    /// <summary>Batch, mandatory — never call <see cref="ResolveActorAsync"/> in a loop over a list of
    /// users. One set-based lookup per branch for the whole batch. Users with no CRM identity are
    /// simply absent from the result rather than throwing, because the batch caller is building a
    /// display list, not authorizing a single request.</summary>
    Task<IReadOnlyDictionary<string, CrmActor>> ResolveActorsAsync(
        IReadOnlyCollection<string> appUserIds, CancellationToken ct);
}
