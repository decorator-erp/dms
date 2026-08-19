using SharedKernel;

namespace Finance.Domain;

/// <summary>
/// Ported from <c>../backend/Domain/Entities/CostCategory.cs</c>. A global cost classification — the
/// chart-of-accounts dimension a <see cref="BudgetLine"/> is filed under. Global master data: no
/// project, no site, no owner, which is why nothing here is scoped.
///
/// <para>The only finance entity carrying no money column at all.</para>
/// </summary>
internal sealed class CostCategory : Entity, IAuditable
{
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }

    private CostCategory()
    {
        // EF materialization.
    }

    public static Result<CostCategory> Create(string code, string name, string? description = null)
    {
        if (string.IsNullOrWhiteSpace(code))
            return CostCategoryErrors.CodeRequired;

        if (string.IsNullOrWhiteSpace(name))
            return CostCategoryErrors.NameRequired;

        return new CostCategory { Code = code, Name = name, Description = description };
    }

    /// <summary>Uniqueness of <see cref="Code"/> is enforced by the handler against the DbContext and
    /// by a unique index — an entity cannot see its own siblings.</summary>
    public Result Update(string name, string? description)
    {
        if (string.IsNullOrWhiteSpace(name))
            return CostCategoryErrors.NameRequired;

        Name = name;
        Description = description;
        return Result.Success();
    }

    public void Activate() => SetActive(true);

    public void Deactivate() => SetActive(false);
}
