using SharedKernel;

namespace Crm.Domain;

/// <summary>
/// P2-A: a dated post the office writes for the client ("Kitchen tiles installed"), optionally with attached shares
/// (images and documents already shared on the same project). A draft until <see cref="Publish"/>; the client sees
/// only published, live posts, and only while the Updates section is on. Deleting is a soft delete.
/// </summary>
internal sealed class CrmPortalUpdate : Entity, IAuditable
{
    public const int TitleMaxLength = 200;
    public const int BodyMaxLength = 10_000;

    private readonly List<CrmPortalUpdateShare> _attachments = [];

    public Guid ProjectId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Body { get; private set; } = string.Empty;
    public DateTime? PublishedAtUtc { get; private set; }
    public Guid AuthorId { get; private set; }

    public IReadOnlyCollection<CrmPortalUpdateShare> Attachments => _attachments.AsReadOnly();

    public bool IsPublished => PublishedAtUtc is not null;

    private CrmPortalUpdate() { }

    public static Result<CrmPortalUpdate> Create(
        Guid projectId, string title, string body, Guid authorId, IReadOnlyList<Guid> shareIds)
    {
        if (projectId == Guid.Empty || authorId == Guid.Empty)
        {
            return CrmPortalShareErrors.InvalidItem;
        }

        var update = new CrmPortalUpdate { ProjectId = projectId, AuthorId = authorId };
        Result edited = update.Edit(title, body, shareIds);
        return edited.IsFailure ? edited.Error : update;
    }

    /// <summary>Title, body and the attachments (replaced as a whole, kept in the given order, each share once).
    /// Allowed after publishing too — a typo is fixed in place, the publish date stays.</summary>
    public Result Edit(string title, string body, IReadOnlyList<Guid> shareIds)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return CrmPortalShareErrors.UpdateTitleRequired;
        }

        if (title.Trim().Length > TitleMaxLength)
        {
            return CrmPortalShareErrors.TitleTooLong;
        }

        if ((body ?? string.Empty).Length > BodyMaxLength)
        {
            return CrmPortalShareErrors.BodyTooLong;
        }

        Title = title.Trim();
        Body = body ?? string.Empty;

        Guid[] ordered = [.. shareIds.Where(id => id != Guid.Empty).Distinct()];
        _attachments.RemoveAll(a => !ordered.Contains(a.ShareId));
        for (int i = 0; i < ordered.Length; i++)
        {
            CrmPortalUpdateShare? existing = _attachments.Find(a => a.ShareId == ordered[i]);
            if (existing is null)
            {
                _attachments.Add(CrmPortalUpdateShare.Create(Id, ordered[i], i));
            }
            else
            {
                existing.MoveTo(i);
            }
        }

        return Result.Success();
    }

    public Result Publish(DateTime nowUtc)
    {
        if (IsPublished)
        {
            return CrmPortalShareErrors.AlreadyPublished;
        }

        PublishedAtUtc = nowUtc;
        return Result.Success();
    }

    public void Delete() => SetActive(false);
}

/// <summary>P2-A: one share attached to a <see cref="CrmPortalUpdate"/>, in display order.</summary>
internal sealed class CrmPortalUpdateShare : Entity, IAuditable
{
    public Guid UpdateId { get; private set; }
    public Guid ShareId { get; private set; }
    public int SortOrder { get; private set; }

    private CrmPortalUpdateShare() { }

    public static CrmPortalUpdateShare Create(Guid updateId, Guid shareId, int sortOrder) =>
        new() { UpdateId = updateId, ShareId = shareId, SortOrder = sortOrder };

    public void MoveTo(int sortOrder) => SortOrder = sortOrder;
}
