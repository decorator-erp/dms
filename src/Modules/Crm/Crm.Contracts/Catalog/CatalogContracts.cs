using BuildingBlocks.Messaging;

namespace Crm.Contracts.Catalog;

public sealed record CrmCatalogCategoryResponse(
    Guid Id, Guid? ParentId, string Code, string NameEn, string NameAr,
    CatalogSelectionMode SelectionMode, bool IsRequired, int SortOrder, string Path, int Depth, bool IsActive);

public sealed record CrmCatalogItemResponse(
    Guid Id, Guid CategoryId, string Code, string NameEn, string NameAr, bool IsPurchasable,
    int SortOrder, bool IsActive);

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
    int SortOrder = 0) : ICommand<Guid>;

public sealed record MoveCrmCatalogCategoryCommand(Guid CategoryId, Guid? NewParentId) : ICommand;

public sealed record CreateCrmCatalogItemCommand(
    Guid CategoryId, string Code, string NameEn, string NameAr, bool IsPurchasable = true,
    int SortOrder = 0) : ICommand<Guid>;

public sealed record CreateCrmCatalogAttributeCommand(
    Guid CategoryId, string Code, string NameEn, string NameAr, CatalogAttributeType DataType,
    string? Unit = null, bool IsRequired = false, int SortOrder = 0) : ICommand<Guid>;

public sealed record CreateCrmCatalogAttributeOptionCommand(
    Guid AttributeId, string Code, string NameEn, string NameAr, int SortOrder = 0) : ICommand<Guid>;

/// <summary>Replace-all per item — there is no per-value update, matching
/// <c>CrmCatalogItemAttributeValue</c>'s own "corrections are replace-all" rule.</summary>
public sealed record SetCrmCatalogItemAttributeValuesCommand(
    Guid ItemId, IReadOnlyList<CatalogAttributeValueData> Values) : ICommand;

public sealed record MapCrmCatalogCategoryToSpaceTypeCommand(Guid CategoryId, Guid SpaceTypeId) : ICommand;

public sealed record GetCrmCatalogTreeQuery : IQuery<IReadOnlyList<CrmCatalogCategoryResponse>>;

public sealed record GetCrmCatalogItemsByCategoryQuery(Guid CategoryId)
    : IQuery<IReadOnlyList<CrmCatalogItemResponse>>;

public sealed record GetCrmCatalogAttributesByCategoryQuery(Guid CategoryId)
    : IQuery<IReadOnlyList<CrmCatalogAttributeResponse>>;
