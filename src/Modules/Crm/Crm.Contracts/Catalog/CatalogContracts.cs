using BuildingBlocks.Messaging;

namespace Crm.Contracts.Catalog;

public sealed record CrmCatalogCategoryResponse(
    Guid Id, Guid? ParentId, string Code, string NameEn, string NameAr,
    CatalogSelectionMode SelectionMode, bool IsRequired, int SortOrder, string Path, int Depth, bool IsActive,
    string? DescriptionEn, string? DescriptionAr, string? ImageUri);

public sealed record CrmCatalogItemResponse(
    Guid Id, Guid CategoryId, string Code, string NameEn, string NameAr, bool IsPurchasable,
    int SortOrder, bool IsActive, string? DescriptionEn, string? DescriptionAr, string? ImageUri);

/// <summary>S10: one catalog item with its attribute values (replace-all through the existing
/// <c>PUT /api/crm/catalog/items/{itemId}/attribute-values</c>).</summary>
public sealed record CrmCatalogItemDetailResponse(
    Guid Id, Guid CategoryId, string Code, string NameEn, string NameAr, bool IsPurchasable,
    int SortOrder, bool IsActive, string? DescriptionEn, string? DescriptionAr, string? ImageUri,
    IReadOnlyList<CatalogAttributeValueData> AttributeValues);

public sealed record CrmCatalogAttributeResponse(
    Guid Id, Guid CategoryId, string Code, string NameEn, string NameAr, CatalogAttributeType DataType,
    string? Unit, bool IsRequired, int SortOrder, bool IsActive);

public sealed record CrmCatalogAttributeOptionResponse(
    Guid Id, Guid AttributeId, string Code, string NameEn, string NameAr, int SortOrder, bool IsActive);

/// <summary>Exactly one of the five value fields is populated, and it must be the one
/// <see cref="CatalogAttributeType"/> dictates on the referenced attribute — validated by
/// <c>CatalogAttributeValueRules</c>, shared between the item-level and space-item-level tables.</summary>
public sealed record CatalogAttributeValueData(
    Guid AttributeId, string? ValueText = null, decimal? ValueNumber = null, bool? ValueBool = null,
    DateOnly? ValueDate = null, Guid? OptionId = null);

public sealed record CreateCrmCatalogCategoryCommand(
    Guid? ParentId, string Code, string NameEn, string NameAr,
    CatalogSelectionMode SelectionMode = CatalogSelectionMode.None, bool IsRequired = false,
    int SortOrder = 0, string? DescriptionEn = null, string? DescriptionAr = null, string? ImageUri = null)
    : ICommand<Guid>;

public sealed record MoveCrmCatalogCategoryCommand(Guid CategoryId, Guid? NewParentId) : ICommand;

public sealed record CreateCrmCatalogItemCommand(
    Guid CategoryId, string Code, string NameEn, string NameAr, bool IsPurchasable = true,
    int SortOrder = 0) : ICommand<Guid>;

public sealed record CreateCrmCatalogAttributeCommand(
    Guid CategoryId, string Code, string NameEn, string NameAr, CatalogAttributeType DataType,
    string? Unit = null, bool IsRequired = false, int SortOrder = 0) : ICommand<Guid>;

public sealed record CreateCrmCatalogAttributeOptionCommand(
    Guid AttributeId, string Code, string NameEn, string NameAr, int SortOrder = 0, string? ImageUri = null)
    : ICommand<Guid>;

/// <summary>Replace-all per item — there is no per-value update, matching
/// <c>CrmCatalogItemAttributeValue</c>'s own "corrections are replace-all" rule.</summary>
public sealed record SetCrmCatalogItemAttributeValuesCommand(
    Guid ItemId, IReadOnlyList<CatalogAttributeValueData> Values) : ICommand;

public sealed record MapCrmCatalogCategoryToSpaceTypeCommand(Guid CategoryId, Guid SpaceTypeId) : ICommand;

/// <remarks>crm-complete F4: with <see cref="SpaceTypeId"/> the tree is a picker — only active categories
/// targeted to that space type (directly or through an ancestor).</remarks>
public sealed record GetCrmCatalogTreeQuery(Guid? SpaceTypeId = null) : IQuery<IReadOnlyList<CrmCatalogCategoryResponse>>;

/// <summary>crm-complete F4 (D4): the four catalog entry kinds the admin routes take as <c>{kind}</c>.</summary>
public enum CrmCatalogEntryKind
{
    Category = 1,
    Item = 2,
    Attribute = 3,
    Option = 4,
}

/// <summary>F4: edit a catalog entry. The fields a kind doesn't have are ignored; a left-out optional field keeps
/// its value.</summary>
public sealed record UpdateCrmCatalogEntryCommand(
    CrmCatalogEntryKind Kind, Guid Id, string Code, string NameEn, string NameAr, CatalogSelectionMode? SelectionMode,
    bool? IsRequired, bool? IsPurchasable, string? Unit, string? DescriptionEn, string? DescriptionAr, string? ImageUri)
    : ICommand;

public sealed record SetCrmCatalogEntryActiveCommand(CrmCatalogEntryKind Kind, Guid Id, bool IsActive) : ICommand;

/// <summary>F4 (D4): hard delete only when nothing references the entry; otherwise 409 <c>crm.catalog.InUse</c>.</summary>
public sealed record DeleteCrmCatalogEntryCommand(CrmCatalogEntryKind Kind, Guid Id) : ICommand;

public sealed record GetCrmCatalogAttributeOptionsQuery(Guid AttributeId, bool IncludeInactive = false)
    : IQuery<IReadOnlyList<CrmCatalogAttributeOptionResponse>>;

public sealed record UnmapCrmCatalogCategoryFromSpaceTypeCommand(Guid CategoryId, Guid SpaceTypeId) : ICommand;

public sealed record GetCrmCatalogItemsByCategoryQuery(Guid CategoryId)
    : IQuery<IReadOnlyList<CrmCatalogItemResponse>>;

/// <summary>S10: 404 <c>crm.catalog.ItemNotFound</c> when the item does not exist.</summary>
public sealed record GetCrmCatalogItemQuery(Guid ItemId) : IQuery<CrmCatalogItemDetailResponse>;

public sealed record GetCrmCatalogAttributesByCategoryQuery(Guid CategoryId)
    : IQuery<IReadOnlyList<CrmCatalogAttributeResponse>>;
