namespace Crm.Contracts;

/// <summary>What a client-scoped CRM message names, from which <c>CrmClientScopeDecorator</c> resolves
/// the client it belongs to.</summary>
public enum CrmClientScopeTargetKind
{
    Client = 1,
    Contact = 2,
    Note = 3,
}

public sealed record CrmClientScopeTarget(CrmClientScopeTargetKind Kind, Guid Id)
{
    public static CrmClientScopeTarget Client(Guid clientId) => new(CrmClientScopeTargetKind.Client, clientId);
    public static CrmClientScopeTarget Contact(Guid contactId) => new(CrmClientScopeTargetKind.Contact, contactId);
    public static CrmClientScopeTarget Note(Guid noteId) => new(CrmClientScopeTargetKind.Note, noteId);
}

/// <summary>
/// C-1 (LC-M2): marker for an office message about one client — the client record, its children, its
/// contacts and its notes. <c>CrmClientScopeDecorator</c> resolves <see cref="ScopeTarget"/> to its client
/// and lets the message through when the caller:
/// <list type="number">
/// <item><description>holds <see cref="CrmPermissions.Clients.ReadAll"/>; or</description></item>
/// <item><description>can reach at least one of the client's projects (<c>IProjectAccessGuard</c>, which
/// itself passes on <c>project.read_all</c>); or</description></item>
/// <item><description>the client has no project at all yet — the pre-project pipeline is visible to any
/// <c>clients.read</c> holder (owner amendment M2).</description></item>
/// </list>
/// Otherwise the message is a 404, per <c>CLAUDE.md</c>'s authorization order. <see cref="UserId"/> is
/// filled by the endpoint from <c>ICurrentUser</c>, never from the request body.
/// </summary>
public interface ICrmClientScoped
{
    string UserId { get; }
    CrmClientScopeTarget ScopeTarget { get; }
}
