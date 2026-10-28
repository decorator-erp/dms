using BuildingBlocks.Messaging;

namespace Crm.Contracts.Clients;

/// <summary>Command/query wrapper types and DTOs for CRM's Client feature, living here rather than in
/// <c>Crm.Application</c> for the reason <c>docs/adding-a-module.md</c>'s "fifth thing" note
/// explains — <c>Crm.Infrastructure</c>'s endpoints need to name these exact types, and
/// <c>Crm.Application</c> already has a real <c>ProjectReference</c> to <c>Crm.Infrastructure</c>.</summary>
public sealed record ClientChildResponse(Guid Id, char Gender, short Age);

public sealed record ClientResponse(
    Guid Id,
    string AppUserId,
    string Address,
    string? Notes,
    string? JobTitle,
    string? MaritalStatus,
    string? RepresentativeName,
    IReadOnlyList<ClientChildResponse> Children,
    bool IsActive);

/// <summary>
/// <b>The only path that creates a client.</b> Lead conversion attaches a lead to a client that already
/// exists (<c>ConvertCrmLeadCommand</c> takes an <c>ExistingClientId</c> and fails without one) — the
/// doc comment that used to claim it "creates the row internally" was wrong.
///
/// <para>Creating a client creates their portal login too. There is no <c>AppUserId</c> to pass: the
/// handler provisions one through <c>Identity.Contracts.IUserProvisioning</c> and hands it back through
/// <c>ClientCreatedIntegrationEvent</c>. The login is <c>UserKind.Client</c> and inactive until that
/// event is delivered — see <c>CreateClientCommandHandler</c> for why the order matters.</para>
///
/// <para><b>This record carries a password.</b> It must never be logged or serialized into an audit
/// row — see <c>LoggingDecorator</c> and <c>AuditRedaction</c>.</para>
/// </summary>
public sealed record CreateClientCommand(
    string Email,
    string Password,
    string FirstName,
    string LastName,
    string Address,
    string? PhoneNumber = null,
    string? Notes = null) : ICommand<Guid>;

public sealed record UpdateClientProfileCommand(
    Guid ClientId, string UserId, string? JobTitle = null, string? MaritalStatus = null, string? RepresentativeName = null)
    : ICommand, ICrmClientScoped
{
    CrmClientScopeTarget ICrmClientScoped.ScopeTarget => CrmClientScopeTarget.Client(ClientId);
}

public sealed record SetClientActiveCommand(Guid ClientId, bool IsActive, string UserId) : ICommand, ICrmClientScoped
{
    CrmClientScopeTarget ICrmClientScoped.ScopeTarget => CrmClientScopeTarget.Client(ClientId);
}

public sealed record DeleteClientCommand(Guid ClientId, string UserId) : ICommand, ICrmClientScoped
{
    CrmClientScopeTarget ICrmClientScoped.ScopeTarget => CrmClientScopeTarget.Client(ClientId);
}

public sealed record AddClientChildCommand(Guid ClientId, char Gender, short Age, string UserId) : ICommand<Guid>, ICrmClientScoped
{
    CrmClientScopeTarget ICrmClientScoped.ScopeTarget => CrmClientScopeTarget.Client(ClientId);
}

public sealed record UpdateClientChildCommand(Guid ClientId, Guid ChildId, char Gender, short Age, string UserId) : ICommand, ICrmClientScoped
{
    CrmClientScopeTarget ICrmClientScoped.ScopeTarget => CrmClientScopeTarget.Client(ClientId);
}

public sealed record RemoveClientChildCommand(Guid ClientId, Guid ChildId, string UserId) : ICommand, ICrmClientScoped
{
    CrmClientScopeTarget ICrmClientScoped.ScopeTarget => CrmClientScopeTarget.Client(ClientId);
}

public sealed record GetClientByIdQuery(Guid ClientId, string UserId) : IQuery<ClientResponse>, ICrmClientScoped
{
    CrmClientScopeTarget ICrmClientScoped.ScopeTarget => CrmClientScopeTarget.Client(ClientId);
}

public sealed record GetClientsQuery : IQuery<IReadOnlyList<ClientResponse>>;
