using SharedKernel;

namespace Crm.Domain;

/// <summary>
/// P2-A: one item the office shared with the client in the portal. Nothing is shared until someone shares it.
///
/// <para><see cref="ItemId"/> is a bare Guid into the owning module (a document, a site-photo link, a stage, a task):
/// no foreign key, the same reason as <c>CrmArtifactDocument.DocumentId</c> (crm migrates before those schemas). It
/// is checked against that module's contract when the share is created, and re-resolved on every read, so a share
/// whose source is deleted simply stops showing.</para>
///
/// <para><b>Follow latest version.</b> A document or image share stores the lineage root
/// (<see cref="RootDocumentId"/>) next to the revision that was picked. With <see cref="FollowLatest"/> (the default)
/// the portal serves the newest active revision of that lineage; without it, exactly <see cref="ItemId"/>. Other kinds
/// never follow.</para>
///
/// <para>Unsharing is a soft delete (<see cref="Unshare"/>). At most one ACTIVE share per (project, kind, item); an
/// item unshared and shared again gets a new row.</para>
/// </summary>
internal sealed class CrmPortalShare : Entity, IAuditable
{
    public const int TitleMaxLength = 200;
    public const int NoteMaxLength = 2000;

    public Guid ProjectId { get; private set; }
    public PortalShareKind ItemKind { get; private set; }
    public Guid ItemId { get; private set; }
    public Guid? RootDocumentId { get; private set; }
    public bool FollowLatest { get; private set; }
    public string? ClientTitle { get; private set; }
    public string? ClientNote { get; private set; }
    public int SortOrder { get; private set; }
    public Guid SharedById { get; private set; }
    public DateTime SharedAtUtc { get; private set; }

    private CrmPortalShare() { }

    public static bool IsDocumentKind(PortalShareKind kind) => kind is PortalShareKind.Document or PortalShareKind.Image;

    public static Result<CrmPortalShare> Create(
        Guid projectId, PortalShareKind kind, Guid itemId, Guid? rootDocumentId, bool followLatest,
        string? clientTitle, string? clientNote, int sortOrder, Guid sharedById, DateTime nowUtc)
    {
        if (projectId == Guid.Empty || itemId == Guid.Empty || sharedById == Guid.Empty || !Enum.IsDefined(kind)
            || IsDocumentKind(kind) && rootDocumentId is null)
        {
            return CrmPortalShareErrors.InvalidItem;
        }

        Result text = CheckText(clientTitle, clientNote);
        if (text.IsFailure)
        {
            return text.Error;
        }

        return new CrmPortalShare
        {
            ProjectId = projectId,
            ItemKind = kind,
            ItemId = itemId,
            RootDocumentId = IsDocumentKind(kind) ? rootDocumentId : null,
            FollowLatest = IsDocumentKind(kind) && followLatest,
            ClientTitle = Clean(clientTitle),
            ClientNote = Clean(clientNote),
            SortOrder = sortOrder,
            SharedById = sharedById,
            SharedAtUtc = nowUtc,
        };
    }

    /// <summary>The client-facing title and note, and (documents and images only) whether to follow the lineage.</summary>
    public Result Edit(string? clientTitle, string? clientNote, bool followLatest)
    {
        Result text = CheckText(clientTitle, clientNote);
        if (text.IsFailure)
        {
            return text;
        }

        ClientTitle = Clean(clientTitle);
        ClientNote = Clean(clientNote);
        FollowLatest = IsDocumentKind(ItemKind) && followLatest;
        return Result.Success();
    }

    public void MoveTo(int sortOrder) => SortOrder = sortOrder;

    public void Unshare() => SetActive(false);

    /// <summary>The section whose switch gates this share.</summary>
    public PortalSection Section => CrmPortalVisibility.SectionOf(ItemKind);

    private static Result CheckText(string? title, string? note)
    {
        if (title is { Length: > TitleMaxLength })
        {
            return CrmPortalShareErrors.TitleTooLong;
        }

        return note is { Length: > NoteMaxLength } ? CrmPortalShareErrors.NoteTooLong : Result.Success();
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
