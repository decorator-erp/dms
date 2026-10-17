using SharedKernel;

namespace Crm.Domain;

/// <summary>
/// Ported from <c>../backend/Domain/Entities/CrmSpaceType.cs</c> — bilingual space-type lookup, the
/// data replacement for a retired enum. Referenced by <see cref="CrmSpace"/> and catalog category
/// space-type mappings.
/// </summary>
internal sealed class CrmSpaceType : Entity, IAuditable
{
    public string Code { get; private set; } = string.Empty;
    public string NameEn { get; private set; } = string.Empty;
    public string NameAr { get; private set; } = string.Empty;
    public int SortOrder { get; private set; }

    private CrmSpaceType() { }

    public static Result<CrmSpaceType> Create(string code, string nameEn, string nameAr, int sortOrder = 0)
    {
        Result validation = Validate(code, nameEn, nameAr);
        if (validation.IsFailure)
        {
            return validation.Error;
        }

        return new CrmSpaceType { Code = code, NameEn = nameEn, NameAr = nameAr, SortOrder = sortOrder };
    }

    /// <summary>Code is immutable — callers resolve types by code.</summary>
    public Result Update(string nameEn, string nameAr, int sortOrder)
    {
        if (string.IsNullOrWhiteSpace(nameEn) || string.IsNullOrWhiteSpace(nameAr))
        {
            return CrmSpaceTypeErrors.NameRequired;
        }

        NameEn = nameEn;
        NameAr = nameAr;
        SortOrder = sortOrder;
        return Result.Success();
    }

    public void Deactivate() => SetActive(false);

    public void Activate() => SetActive(true);

    private static Result Validate(string code, string nameEn, string nameAr)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return CrmSpaceTypeErrors.CodeRequired;
        }

        if (string.IsNullOrWhiteSpace(nameEn) || string.IsNullOrWhiteSpace(nameAr))
        {
            return CrmSpaceTypeErrors.NameRequired;
        }

        return Result.Success();
    }
}
