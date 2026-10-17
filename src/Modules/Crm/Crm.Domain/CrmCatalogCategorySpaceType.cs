using SharedKernel;

namespace Crm.Domain;

/// <summary>
/// Ported from <c>../backend/Domain/Entities/CrmCatalogCategorySpaceType.cs</c> — targeting: which
/// space types a catalog category is offered for. A mapping on a category cascades to its whole
/// subtree and their items (resolved by the Application read side, G9-11). Only categories can be
/// mapped — a category with no mapping anywhere on its ancestor chain is global.
/// </summary>
internal sealed class CrmCatalogCategorySpaceType : Entity, IAuditable
{
    public Guid CategoryId { get; private set; }
    public Guid SpaceTypeId { get; private set; }

    private CrmCatalogCategorySpaceType() { }

    public static Result<CrmCatalogCategorySpaceType> Create(Guid categoryId, Guid spaceTypeId)
    {
        if (categoryId == Guid.Empty)
        {
            return CrmCatalogErrors.CategoryNotFound;
        }

        if (spaceTypeId == Guid.Empty)
        {
            return CrmCatalogErrors.SpaceTypeNotFound;
        }

        return new CrmCatalogCategorySpaceType { CategoryId = categoryId, SpaceTypeId = spaceTypeId };
    }
}
