using BuildingBlocks.Messaging;

namespace Crm.Contracts.Spaces;

/// <summary>crm-complete F6 (D3): the option chosen for one attribute.</summary>
public sealed record SpaceSelectionOptionInput(Guid AttributeId, Guid OptionId);

public sealed record CrmSpaceSelectionOptionResponse(
    Guid AttributeId, string AttributeNameEn, string AttributeNameAr, Guid OptionId, string OptionCode, string OptionNameEn,
    string OptionNameAr);

/// <summary>F6: a storefront pick with the catalog names resolved. The same shape is the brief-space entry's
/// snapshot (<c>CrmBriefSpaceResponse.Selections</c>), frozen when the entry is sent.</summary>
public sealed record CrmSpaceSelectionResponse(
    Guid Id, Guid SpaceId, Guid CatalogItemId, string ItemCode, string ItemNameEn, string ItemNameAr, decimal Quantity,
    string? Notes, int SortOrder, IReadOnlyList<CrmSpaceSelectionOptionResponse> Options);

public sealed record GetCrmSpaceSelectionsQuery(Guid SpaceId, string UserId)
    : IQuery<IReadOnlyList<CrmSpaceSelectionResponse>>, ICrmProjectScoped
{
    CrmScopeTarget ICrmProjectScoped.ScopeTarget => CrmScopeTarget.Space(SpaceId);
}

public sealed record AddCrmSpaceSelectionCommand(
    Guid SpaceId, string UserId, Guid CatalogItemId, decimal Quantity, string? Notes, IReadOnlyList<SpaceSelectionOptionInput> Options)
    : ICommand<Guid>, ICrmProjectScoped
{
    CrmScopeTarget ICrmProjectScoped.ScopeTarget => CrmScopeTarget.Space(SpaceId);
}

/// <summary>F6. Null <see cref="Options"/> keeps the chosen options; an empty list clears them.</summary>
public sealed record UpdateCrmSpaceSelectionCommand(
    Guid SpaceId, Guid SelectionId, string UserId, decimal Quantity, string? Notes, IReadOnlyList<SpaceSelectionOptionInput>? Options)
    : ICommand, ICrmProjectScoped
{
    CrmScopeTarget ICrmProjectScoped.ScopeTarget => CrmScopeTarget.Space(SpaceId);
}

public sealed record RemoveCrmSpaceSelectionCommand(Guid SpaceId, Guid SelectionId, string UserId) : ICommand, ICrmProjectScoped
{
    CrmScopeTarget ICrmProjectScoped.ScopeTarget => CrmScopeTarget.Space(SpaceId);
}

public sealed record ReorderCrmSpaceSelectionsCommand(Guid SpaceId, IReadOnlyList<Guid> SelectionIds, string UserId)
    : ICommand, ICrmProjectScoped
{
    CrmScopeTarget ICrmProjectScoped.ScopeTarget => CrmScopeTarget.Space(SpaceId);
}
