using SharedKernel;

namespace Projects.Domain;

/// <summary>One picked template stage. <see cref="SourceStageId"/> is a plain id, no foreign key: the
/// template stage may be deleted later, and confirm then refuses with
/// <see cref="ScopeOfWorkErrors.SourceStageNotFound"/>. <see cref="CreatedStageId"/> is the
/// <c>ProjectStage</c> confirm created from it. Changed only through <see cref="ScopeOfWork"/>.</summary>
internal sealed class ScopeOfWorkItem : Entity
{
    public Guid ScopeOfWorkId { get; private set; }
    public Guid SourceStageId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public int Order { get; private set; }
    public decimal Fee { get; private set; }
    public Guid? CreatedStageId { get; private set; }

    private ScopeOfWorkItem() { }

    internal static ScopeOfWorkItem Create(
        Guid scopeOfWorkId, Guid sourceStageId, string name, string? description, int order, decimal fee) =>
        new()
        {
            ScopeOfWorkId = scopeOfWorkId,
            SourceStageId = sourceStageId,
            Name = name,
            Description = description,
            Order = order,
            Fee = fee,
        };

    internal void Update(string name, string? description, decimal fee)
    {
        Name = name;
        Description = description;
        Fee = fee;
    }

    internal void SetOrder(int order) => Order = order;

    internal void SetCreatedStage(Guid projectStageId) => CreatedStageId = projectStageId;

    /// <summary>P12: the stage confirm created from this item was deleted.</summary>
    internal void ClearCreatedStage() => CreatedStageId = null;
}
