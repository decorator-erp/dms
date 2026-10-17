using SharedKernel;

namespace Crm.Domain;

/// <summary>
/// Ported from <c>../backend/Domain/Entities/CrmSpace.cs</c> — a real room on a project, the brief's
/// unit of authorship and the board the catalog is recorded onto. <see cref="SpaceTypeId"/> stays the
/// category; this is the instance ("Kids room 1" vs "Kids room 2" are two spaces of one type).
/// <see cref="Name"/>/<see cref="Description"/> stay single-language deliberately — user-authored per
/// project, not catalog reference data, unlike everything else in CRM's catalog which is bilingual.
/// </summary>
internal sealed class CrmSpace : Entity, IAuditable
{
    public Guid ProjectId { get; private set; }
    public Guid SpaceTypeId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public int SortOrder { get; private set; }

    private CrmSpace() { }

    public static Result<CrmSpace> Create(
        Guid projectId, Guid spaceTypeId, string name, int sortOrder = 0, string? description = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return CrmSpaceErrors.InvalidData;
        }

        return new CrmSpace
        {
            ProjectId = projectId,
            SpaceTypeId = spaceTypeId,
            Name = name.Trim(),
            SortOrder = sortOrder,
            Description = NormalizeDescription(description),
        };
    }

    /// <summary>Full replacement, including <paramref name="description"/> — omitting it clears it,
    /// there is no "leave unchanged" meaning for null here.</summary>
    public Result Update(Guid spaceTypeId, string name, string? description = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return CrmSpaceErrors.InvalidData;
        }

        SpaceTypeId = spaceTypeId;
        Name = name.Trim();
        Description = NormalizeDescription(description);
        return Result.Success();
    }

    private static string? NormalizeDescription(string? description)
    {
        string? trimmed = description?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    public void SetSortOrder(int sortOrder) => SortOrder = sortOrder;

    public void Deactivate() => SetActive(false);

    public void Activate() => SetActive(true);
}
