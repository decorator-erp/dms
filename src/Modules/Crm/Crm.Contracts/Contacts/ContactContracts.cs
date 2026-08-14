using BuildingBlocks.Messaging;

namespace Crm.Contracts.Contacts;

public sealed record CrmContactResponse(
    Guid Id, Guid ClientId, string FirstName, string LastName, string? Email, string? Phone,
    string? Mobile, ContactRole Role, bool IsPrimary, string? Notes);

public sealed record CreateCrmContactCommand(
    Guid ClientId, string FirstName, string LastName, ContactRole Role, bool IsPrimary,
    string? Email = null, string? Phone = null, string? Mobile = null, string? Notes = null)
    : ICommand<Guid>;

public sealed record UpdateCrmContactCommand(
    Guid ContactId, string FirstName, string LastName, ContactRole Role,
    string? Email, string? Phone, string? Mobile, string? Notes) : ICommand;

/// <summary>Exactly one contact per client may be primary — the handler demotes the previous holder
/// in the same transaction, mirroring <c>CrmContactDomainService</c>'s backend behavior.</summary>
public sealed record SetPrimaryCrmContactCommand(Guid ClientId, Guid ContactId) : ICommand;

public sealed record GetCrmContactsByClientQuery(Guid ClientId) : IQuery<IReadOnlyList<CrmContactResponse>>;
