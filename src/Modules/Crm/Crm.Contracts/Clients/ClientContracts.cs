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

/// <summary>Direct creation path — the other is lead conversion (<c>ConvertLeadCommand</c>), which
/// creates the row internally rather than through this command.</summary>
public sealed record CreateClientCommand(string AppUserId, string Address, string? Notes = null)
    : ICommand<Guid>;

public sealed record UpdateClientProfileCommand(
    Guid ClientId, string? JobTitle = null, string? MaritalStatus = null, string? RepresentativeName = null)
    : ICommand;

public sealed record SetClientActiveCommand(Guid ClientId, bool IsActive) : ICommand;

public sealed record DeleteClientCommand(Guid ClientId) : ICommand;

public sealed record AddClientChildCommand(Guid ClientId, char Gender, short Age) : ICommand<Guid>;

public sealed record UpdateClientChildCommand(Guid ClientId, Guid ChildId, char Gender, short Age) : ICommand;

public sealed record RemoveClientChildCommand(Guid ClientId, Guid ChildId) : ICommand;

public sealed record GetClientByIdQuery(Guid ClientId) : IQuery<ClientResponse>;

public sealed record GetClientsQuery : IQuery<IReadOnlyList<ClientResponse>>;
