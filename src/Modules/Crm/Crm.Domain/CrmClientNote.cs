using SharedKernel;

namespace Crm.Domain;

/// <summary>
/// Ported from <c>../backend/Domain/Entities/CrmClientNote.cs</c> — an internal note about a client,
/// written by an office employee. Never visible to any client: no Portal route maps this feature
/// (G9-14), and <c>ICrmAccessGuard</c>'s client branch has no path to it.
/// </summary>
internal sealed class CrmClientNote : Entity, IAuditable
{
    public Guid ClientId { get; private set; }
    public Guid? ProjectId { get; private set; }
    public string Content { get; private set; } = string.Empty;
    public Guid WrittenById { get; private set; }

    private CrmClientNote() { }

    public static CrmClientNote Create(Guid clientId, Guid? projectId, string content, Guid writtenById) =>
        new()
        {
            ClientId = clientId,
            ProjectId = projectId,
            Content = content,
            WrittenById = writtenById,
        };

    public void Update(string content) => Content = content;
}
