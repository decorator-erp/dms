using BuildingBlocks.Messaging;

namespace Crm.Contracts.Leads;

public sealed record CrmLeadResponse(
    Guid Id, string FirstName, string LastName, string? Email, string? Phone, LeadSource Source,
    LeadStatus Status, Guid OwnerId, string? Notes, Guid? ConvertedClientId, DateTime? ConvertedAtUtc);

public sealed record CrmLeadIntakeResidentialData(
    UnitType? UnitType = null, UnitCondition? UnitCondition = null, bool? IsLicensed = null,
    bool? IsRented = null, FinishingPurpose? FinishingPurpose = null);

public sealed record CrmLeadIntakeData(
    string? JobTitle = null,
    string? ClientAddress = null,
    ProjectSector? Sector = null,
    string? UnitAddress = null,
    decimal? AreaSqm = null,
    bool? UsedDesignerBefore = null,
    PastExperience? DesignerExperience = null,
    string? DesignerExperienceReason = null,
    CrmLeadIntakeResidentialData? Residential = null);

public sealed record CrmLeadIntakeResponse(Guid LeadId, CrmLeadIntakeData Data);

public sealed record CreateCrmLeadCommand(
    string FirstName, string LastName, LeadSource Source, Guid OwnerId,
    string? Email = null, string? Phone = null, string? Notes = null) : ICommand<Guid>;

public sealed record UpdateCrmLeadCommand(
    Guid LeadId, string FirstName, string LastName, LeadSource Source, Guid OwnerId,
    string? Email, string? Phone, string? Notes) : ICommand;

public sealed record ChangeCrmLeadStatusCommand(Guid LeadId, LeadStatus Status) : ICommand;

/// <summary>v1 requires an existing client — same trade the backend made (a
/// <c>// TODO: v2 — accept CreateClientRequest</c> marks new-client-on-convert as unimplemented
/// there too, so this port doesn't invent it). Publishes <c>LeadConvertedIntegrationEvent</c> +
/// <c>ClientCreatedIntegrationEvent</c> is NOT implied here — conversion only fires
/// <c>LeadConvertedIntegrationEvent</c>; <c>ClientCreatedIntegrationEvent</c> fires only when the
/// client row is actually created (see <c>ConvertLeadCommandHandler</c>'s remarks in Application).</summary>
public sealed record ConvertCrmLeadCommand(Guid LeadId, Guid ExistingClientId) : ICommand<Guid>;

public sealed record SaveCrmLeadIntakeCommand(Guid LeadId, CrmLeadIntakeData Data) : ICommand;

public sealed record GetCrmLeadByIdQuery(Guid LeadId) : IQuery<CrmLeadResponse>;

public sealed record GetCrmLeadsQuery : IQuery<IReadOnlyList<CrmLeadResponse>>;

public sealed record GetCrmLeadIntakeQuery(Guid LeadId) : IQuery<CrmLeadIntakeResponse>;
