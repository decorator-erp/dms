using BuildingBlocks.Messaging;

namespace Sites.Contracts.Definition;

// ── Requests ───────────────────────────────────────────────────────────────────

public sealed record CreateSiteModuleRequest(
    string Code, string NameEn, string NameAr,
    string? DescriptionEn = null, string? DescriptionAr = null, int SortOrder = 0);

public sealed record UpdateSiteModuleRequest(
    string NameEn, string NameAr, string? DescriptionEn, string? DescriptionAr, int SortOrder);

public sealed record CreateSiteItemRequest(
    string Code, string NameEn, string NameAr, SiteItemDataType DataType, string? Unit = null,
    decimal? MinValue = null, decimal? MaxValue = null, decimal Tolerance = 0m,
    int? MaxLength = null, string? RegexPattern = null);

public sealed record UpdateSiteItemRequest(
    string NameEn, string NameAr,
    decimal? MinValue, decimal? MaxValue, int? MaxLength, string? RegexPattern);

/// <summary>The four frozen-once-captured fields; refused with 409 once any value references the
/// item.</summary>
public sealed record ChangeSiteItemShapeRequest(
    string Code, SiteItemDataType DataType, string? Unit, decimal Tolerance);

public sealed record SiteItemOptionRequest(string Code, string NameEn, string NameAr, int SortOrder = 0);

public sealed record UpdateSiteItemOptionRequest(string NameEn, string NameAr, int SortOrder);

public sealed record AttachSiteItemRequest(
    Guid ItemId, bool IsRequired = false, bool IsVisible = true, int SortOrder = 0);

public sealed record UpdateSiteLinkRequest(bool IsRequired, bool IsVisible, int SortOrder);

public sealed record ReorderSiteLinksRequest(IReadOnlyList<SiteLinkOrderRequest> Order);

public sealed record SiteLinkOrderRequest(Guid ItemId, int SortOrder);

// ── Responses ──────────────────────────────────────────────────────────────────

public sealed record SiteModuleResponse(
    Guid Id, string Code, string NameEn, string NameAr,
    string? DescriptionEn, string? DescriptionAr, bool IsSystem, int SortOrder, bool IsActive);

public sealed record SiteItemResponse(
    Guid Id, string Code, string NameEn, string NameAr, SiteItemDataType DataType, string? Unit,
    decimal? MinValue, decimal? MaxValue, decimal Tolerance, int? MaxLength, string? RegexPattern,
    bool IsSystem, bool IsActive, IReadOnlyList<SiteItemOptionResponse> Options);

public sealed record SiteItemOptionResponse(
    Guid Id, string Code, string NameEn, string NameAr, int SortOrder, bool IsActive);

/// <summary>
/// One field on a module's form. Requiredness, visibility and order come from the LINK, never the
/// item — <c>mount_height_cm</c> is required in electrical points and optional in openings.
/// </summary>
public sealed record SiteSchemaFieldResponse(
    Guid LinkId, Guid ItemId, string Code, string NameEn, string NameAr,
    SiteItemDataType DataType, string? Unit, decimal? MinValue, decimal? MaxValue, decimal Tolerance,
    int? MaxLength, string? RegexPattern,
    bool IsRequired, bool IsVisible, int SortOrder,
    IReadOnlyList<SiteItemOptionResponse> Options);

public sealed record SiteModuleSchemaResponse(
    Guid ModuleId, string Code, string NameEn, string NameAr,
    string? DescriptionEn, string? DescriptionAr, int SortOrder,
    IReadOnlyList<SiteSchemaFieldResponse> Fields);

/// <summary>Where an item is used — the read behind "can I safely change this?".</summary>
public sealed record SiteItemUsageResponse(
    Guid ItemId, string Code, bool HasCapturedValues, int CapturedValueCount,
    IReadOnlyList<SiteItemUsageModuleResponse> Modules);

public sealed record SiteItemUsageModuleResponse(
    Guid ModuleId, string Code, bool IsRequired, bool IsActive);

// ── Commands ───────────────────────────────────────────────────────────────────
//
// None of these implement ISiteScoped. The definition layer is GLOBAL MASTER DATA with no project and
// no site — the backend's SiteEndpoints doc comment says so explicitly ("the site definition layer…
// is global master data with no project at all, and is deliberately outside this"). They are gated by
// permission alone (SitesPermissions.Definitions.Manage), never by row-level access. See ISiteScoped's
// own remarks on why that exemption is machine-checked rather than trusted.

public sealed record CreateSiteModuleCommand(CreateSiteModuleRequest Request) : ICommand<Guid>;

public sealed record UpdateSiteModuleCommand(Guid ModuleId, UpdateSiteModuleRequest Request) : ICommand;

public sealed record DeleteSiteModuleCommand(Guid ModuleId) : ICommand;

public sealed record SetSiteModuleActiveCommand(Guid ModuleId, bool IsActive) : ICommand;

public sealed record CreateSiteItemCommand(CreateSiteItemRequest Request) : ICommand<Guid>;

public sealed record UpdateSiteItemCommand(Guid ItemId, UpdateSiteItemRequest Request) : ICommand;

/// <summary>Changes code/data type/unit/tolerance — refused once any value references the item.</summary>
public sealed record ChangeSiteItemShapeCommand(Guid ItemId, ChangeSiteItemShapeRequest Request) : ICommand;

public sealed record DeleteSiteItemCommand(Guid ItemId) : ICommand;

public sealed record SetSiteItemActiveCommand(Guid ItemId, bool IsActive) : ICommand;

public sealed record AddSiteItemOptionCommand(Guid ItemId, SiteItemOptionRequest Request) : ICommand<Guid>;

public sealed record UpdateSiteItemOptionCommand(Guid OptionId, UpdateSiteItemOptionRequest Request)
    : ICommand;

public sealed record SetSiteItemOptionActiveCommand(Guid OptionId, bool IsActive) : ICommand;

public sealed record AttachSiteItemCommand(Guid ModuleId, AttachSiteItemRequest Request) : ICommand<Guid>;

public sealed record UpdateSiteLinkCommand(Guid ModuleId, Guid ItemId, UpdateSiteLinkRequest Request)
    : ICommand;

public sealed record DetachSiteItemCommand(Guid ModuleId, Guid ItemId) : ICommand;

public sealed record ReorderSiteLinksCommand(Guid ModuleId, ReorderSiteLinksRequest Request) : ICommand;

// ── Queries ────────────────────────────────────────────────────────────────────

public sealed record GetSiteModulesQuery(bool IncludeInactive = false)
    : IQuery<IReadOnlyList<SiteModuleResponse>>;

/// <summary>The surveyor's form. Served through <see cref="ISiteSchemaCache"/> — the only query that
/// is.</summary>
public sealed record GetSiteModuleSchemaQuery(string Code) : IQuery<SiteModuleSchemaResponse>;

public sealed record GetSiteItemsQuery(bool IncludeInactive = false)
    : IQuery<IReadOnlyList<SiteItemResponse>>;

public sealed record GetSiteItemUsageQuery(Guid ItemId) : IQuery<SiteItemUsageResponse>;
