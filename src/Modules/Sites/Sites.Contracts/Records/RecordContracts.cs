using BuildingBlocks.Messaging;
using Sites.Contracts.Definition;

namespace Sites.Contracts.Records;

// ── Requests ───────────────────────────────────────────────────────────────────

public sealed record CreateSiteRecordRequest(
    Guid ModuleId, string Label, Guid? SpaceId = null, Guid? CatalogItemId = null,
    int SortOrder = 0, string? Note = null);

/// <summary>
/// Carries no <c>ModuleId</c> — a record's module is fixed at creation. Changing it would orphan every
/// value already captured against the old module's fields, which is why the entity has no setter for
/// it. (The backend's bulk-save request DOES carry one on update rows and then ignores it while still
/// validating against it; that divergence is fixed at S1-07 rather than ported.)
/// </summary>
public sealed record UpdateSiteRecordRequest(
    string Label, Guid? SpaceId, Guid? CatalogItemId, int SortOrder, string? Note);

/// <summary>Exactly one value member is expected to be set, matching the item's declared type.</summary>
public sealed record SiteValueRequest(
    Guid ItemId,
    decimal? ValueNumber = null,
    string? ValueText = null,
    bool? ValueBool = null,
    DateOnly? ValueDate = null,
    Guid? OptionId = null);

/// <summary>
/// Replaces a record's whole value set. Values absent from <see cref="Values"/> are cleared — the
/// value table has no soft-delete flag, and a cleared measurement is not a measurement.
/// </summary>
public sealed record SetSiteRecordValuesRequest(IReadOnlyList<SiteValueRequest> Values);

public sealed record BulkSaveSiteRecordsRequest(IReadOnlyList<BulkSiteRecordRequest> Records);

/// <summary>
/// One row of a grid save. <see cref="ModuleId"/> is authoritative for INSERTS only (where
/// <see cref="RecordId"/> is null); for updates the module is read from the stored row — see
/// <see cref="UpdateSiteRecordRequest"/>.
/// </summary>
public sealed record BulkSiteRecordRequest(
    Guid? RecordId, Guid ModuleId, string Label, Guid? SpaceId, Guid? CatalogItemId,
    int SortOrder, string? Note, IReadOnlyList<SiteValueRequest> Values);

// ── Responses ──────────────────────────────────────────────────────────────────

public sealed record SiteRecordResponse(
    Guid Id, Guid ModuleId, string Label, Guid? SpaceId, string? SpaceName,
    Guid? CatalogItemId, int SortOrder, string? Note);

/// <summary>One pivoted row: the record, plus one cell per field of its module.</summary>
public sealed record SitePivotRowResponse(
    Guid RecordId, string Label, Guid? SpaceId, string? SpaceName, int SortOrder, string? Note,
    IReadOnlyDictionary<string, SitePivotCellResponse?> Cells);

public sealed record SitePivotCellResponse(
    decimal? ValueNumber, string? ValueText, bool? ValueBool, DateOnly? ValueDate,
    Guid? OptionId, string? OptionCode, string? OptionNameEn, string? OptionNameAr);

public sealed record SitePivotResponse(
    Guid SiteId, Guid ModuleId, string ModuleCode,
    IReadOnlyList<SiteSchemaFieldResponse> Columns, IReadOnlyList<SitePivotRowResponse> Rows);

public sealed record SiteGroupedResponse(Guid SiteId, IReadOnlyList<SiteSpaceGroupResponse> Groups);

/// <summary>A null <see cref="SpaceId"/> is the synthetic "Whole unit / عام" bucket, ordered
/// last.</summary>
public sealed record SiteSpaceGroupResponse(
    Guid? SpaceId, string SpaceName, IReadOnlyList<SiteRecordResponse> Records);

// ── Derived reads ──────────────────────────────────────────────────────────────

/// <summary>
/// One derived family — the set of records on this site that measure the same thing.
///
/// <para>🔴 <b><see cref="FamilyKey"/> is opaque. Compare it for equality; never parse it.</b> Its
/// composition — which items, in which order, at which tolerance — changes whenever the module's
/// definition changes, and is <b>not part of the API contract</b>. It reads like structured text
/// (<c>width_cm=90 | quantity=1</c>) and it is at once the tablet's render value and the Revit
/// exporter's join token, so the first regex written over it will work, ship, and then break silently
/// on the day an item is attached to the module or a tolerance is edited. Equality is the contract;
/// shape is not.</para>
/// </summary>
public sealed record SiteFamilyResponse(
    string FamilyKey, int Instances, int SpaceCount, IReadOnlyList<string> Members);

public sealed record SiteFamiliesResponse(
    Guid SiteId, Guid ModuleId, IReadOnlyList<string> GroupBy,
    IReadOnlyList<SiteFamilyResponse> Families);

public sealed record SiteBoqLineResponse(
    string ModuleCode, string KindCode, string NameEn, string NameAr,
    int Occurrences, int Spaces,
    decimal? TotalQty, decimal? TotalAreaSqm, decimal? TotalLengthM);

public sealed record SiteBoqResponse(Guid SiteId, IReadOnlyList<SiteBoqLineResponse> Lines);

// ── Commands ───────────────────────────────────────────────────────────────────

public sealed record AddSiteRecordCommand(
    Guid SiteId, CreateSiteRecordRequest Request, string AppUserId)
    : ICommand<Guid>, ISiteScoped;

public sealed record UpdateSiteRecordCommand(
    Guid SiteId, Guid RecordId, UpdateSiteRecordRequest Request, string AppUserId)
    : ICommand, ISiteScoped;

public sealed record DeleteSiteRecordCommand(Guid SiteId, Guid RecordId, string AppUserId)
    : ICommand, ISiteScoped;

public sealed record SetSiteRecordValuesCommand(
    Guid SiteId, Guid RecordId, SetSiteRecordValuesRequest Request, string AppUserId)
    : ICommand, ISiteScoped;

/// <summary>The grid save: many records and their values in one transaction.</summary>
public sealed record BulkSaveSiteRecordsCommand(
    Guid SiteId, BulkSaveSiteRecordsRequest Request, string AppUserId)
    : ICommand, ISiteScoped;

// ── Queries ────────────────────────────────────────────────────────────────────

public sealed record GetSitePivotQuery(Guid SiteId, Guid ModuleId, string AppUserId)
    : IQuery<SitePivotResponse>, ISiteScoped;

public sealed record GetSiteRecordsGroupedQuery(Guid SiteId, string AppUserId)
    : IQuery<SiteGroupedResponse>, ISiteScoped;

/// <summary><see cref="GroupBy"/> is a comma-separated list of item codes, or the tokens
/// <c>exact</c>/<c>size</c>; null or empty means "every required field".</summary>
public sealed record GetSiteFamiliesQuery(
    Guid SiteId, Guid ModuleId, string? GroupBy, string AppUserId)
    : IQuery<SiteFamiliesResponse>, ISiteScoped;

public sealed record GetSiteBoqQuery(Guid SiteId, string AppUserId)
    : IQuery<SiteBoqResponse>, ISiteScoped;
