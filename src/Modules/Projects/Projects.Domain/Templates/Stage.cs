using SharedKernel;

namespace Projects.Domain;

/// <summary>Ported from <c>../backend/Domain/Entities/Stage.cs</c> — a template stage. Materialized into
/// a runtime <see cref="ProjectStage"/> at instantiation (G5-07).</summary>
internal sealed class Stage : Entity, IAuditable
{
    public Guid StageTemplateId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public int Order { get; private set; }
    public decimal WeightPercent { get; private set; }

    private Stage()
    {
        // EF materialization.
    }

    public static Stage Create(Guid stageTemplateId, string name, int order, decimal weightPercent, string? description = null) =>
        new() { StageTemplateId = stageTemplateId, Name = name, Order = order, WeightPercent = weightPercent, Description = description };

    public void Update(string? name = null, int? order = null, decimal? weightPercent = null, string? description = null)
    {
        if (name is not null)
            Name = name;
        if (order is not null)
            Order = order.Value;
        if (weightPercent is not null)
            WeightPercent = weightPercent.Value;
        if (description is not null)
            Description = description;
    }
}
