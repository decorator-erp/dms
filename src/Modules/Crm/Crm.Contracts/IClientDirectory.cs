namespace Crm.Contracts;

/// <summary>
/// CRM-owned client data only — no join into <c>identity.</c>. <see cref="AppUserId"/> is CRM's own
/// bare-string foreign key to Identity's user id (see <c>Crm.Domain.Client.AppUserId</c>), not a
/// navigation, exactly as <c>Hr.Contracts.EmployeeSummary</c> does for employees.
///
/// <para>Carries no project list. A client does not navigate to projects; projects reference a
/// client. The backend's <c>Client._projects</c> collection is deliberately not ported — dropping it
/// is what makes CRM ownership of <c>Client</c> legal without inverting the module graph.</para>
/// </summary>
public sealed record ClientSummary(
    Guid Id, string AppUserId, string Address, string? RepresentativeName, bool IsActive);

/// <summary>
/// Cross-module read surface for CRM-owned client data. The first caller is
/// <c>Projects.Application</c>'s project-creation handler, which validates <c>Project.ClientId</c>
/// through <see cref="ExistAsync"/> — the application-code half of the FK this phase adds.
///
/// <para>Every lookup has a batch sibling, no exceptions, per <c>docs/adding-a-module.md</c>'s
/// Contracts rule. <see cref="ExistAsync"/> is the batched existence check a validator wants: it
/// returns only the ids that resolve, so a caller diffs against its own input rather than issuing one
/// round-trip per id.</para>
/// </summary>
public interface IClientDirectory
{
    Task<ClientSummary?> GetAsync(Guid clientId, CancellationToken ct);

    Task<IReadOnlyList<ClientSummary>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);

    /// <summary>Which of <paramref name="ids"/> name an existing client. One set-based query; the
    /// caller diffs the result against its input to find the unknown ones.</summary>
    Task<IReadOnlyList<Guid>> ExistAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);

    /// <summary>C-5 (LC-M5): which of <paramref name="ids"/> name an existing, <b>active</b> client — the
    /// check a caller about to attach new work to a client wants.</summary>
    Task<IReadOnlyList<Guid>> ExistActiveAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);

    /// <summary>The client whose portal account is <paramref name="appUserId"/>, or null. The client
    /// half of <see cref="ICrmAccessGuard.ResolveActorAsync"/>'s two-branch resolution, exposed here
    /// because the Portal endpoints resolve the same way.</summary>
    Task<ClientSummary?> ResolveByAppUserIdAsync(string appUserId, CancellationToken ct);
}

