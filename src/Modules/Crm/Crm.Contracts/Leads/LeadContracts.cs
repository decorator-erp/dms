using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;

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
/// client row is actually created, which is <c>CreateClientCommand</c> alone.
///
/// <para>Worth stating for whoever builds v2: creating a client now also provisions its portal login,
/// so "accept a CreateClientRequest on convert" means provisioning an account inside the conversion
/// transaction — go through <c>CreateClientCommand</c>'s handler rather than reimplementing the
/// ordering it documents.</para></summary>
/// <summary>D-9 (C-F1): client-scoped on the target client — a caller who can't see
/// <see cref="ExistingClientId"/> gets 404 <c>crm.clients.NotFound</c>, so convert can't fill an unseen
/// client's blanks or probe which client ids exist.</summary>
public sealed record ConvertCrmLeadCommand(Guid LeadId, Guid ExistingClientId, string UserId) : ICommand<Guid>, ICrmClientScoped
{
    CrmClientScopeTarget ICrmClientScoped.ScopeTarget => CrmClientScopeTarget.Client(ExistingClientId);
}

public sealed record SaveCrmLeadIntakeCommand(Guid LeadId, CrmLeadIntakeData Data) : ICommand;

public sealed record GetCrmLeadByIdQuery(Guid LeadId) : IQuery<CrmLeadResponse>;

/// <summary>Paged, newest first (then by id, so pages are stable). Every filter is optional and they
/// combine: <see cref="Status"/>, <see cref="OwnerId"/>, and <see cref="Search"/> — a case-insensitive
/// contains-match on first name, last name, email and phone, with <c>%</c>/<c>_</c> taken literally.</summary>
public sealed record GetCrmLeadsQuery(
    int PageNumber = 1, int PageSize = 50, LeadStatus? Status = null, Guid? OwnerId = null, string? Search = null)
    : IQuery<PagedResult<CrmLeadResponse>>;

/// <summary>How many leads are in each status, under the same owner and search filters as
/// <see cref="GetCrmLeadsQuery"/> (there is no status filter: the counts are per status).
/// <see cref="Total"/> is the sum.</summary>
public sealed record CrmLeadStatusCountsResponse(int Total, int New, int Contacted, int Qualified, int Unqualified, int Converted);

public sealed record GetCrmLeadStatusCountsQuery(Guid? OwnerId = null, string? Search = null)
    : IQuery<CrmLeadStatusCountsResponse>;

public sealed record GetCrmLeadIntakeQuery(Guid LeadId) : IQuery<CrmLeadIntakeResponse>;
