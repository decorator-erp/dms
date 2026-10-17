using SharedKernel;

namespace Projects.Domain;

/// <summary>Ported from <c>../backend/Domain/Entities/StageTemplate.cs</c>.</summary>
internal sealed class StageTemplate : Entity, IAuditable
{
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public StageTemplateType Type { get; private set; }
    public bool IsDefault { get; private set; }

    private StageTemplate()
    {
        // EF materialization.
    }

    public static StageTemplate Create(
        string name, StageTemplateType type = StageTemplateType.Custom, bool isDefault = true, string? description = null) =>
        new() { Name = name, Type = type, IsDefault = isDefault, Description = description };

    public void Update(string? name = null, string? description = null, bool? isDefault = null)
    {
        if (name is not null)
        {
            Name = name;
        }

        if (description is not null)
        {
            Description = description;
        }

        if (isDefault is not null)
        {
            IsDefault = isDefault.Value;
        }
    }
}
