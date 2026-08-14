using SharedKernel;

namespace Crm.Domain;

/// <summary>
/// Ported from <c>../backend/Domain/Entities/CrmRequirementItem.cs</c> — the atomic unit designers
/// consume. Author is employee XOR contact. Employee-written items need client confirmation;
/// client-written items are trusted as-is (<see cref="ApprovalState.NotRequired"/> — the client's own
/// words, <see cref="RequirementSource.ClientDirect"/>). Ledger rule: locked items
/// (Approved/NotRequired) are never edited — a change appends a new item that supersedes the locked
/// one.
/// </summary>
internal sealed class CrmRequirementItem : Entity, IAuditable
{
    public Guid ProjectId { get; private set; }
    public Guid PeriodId { get; private set; }
    public RequirementCategory Category { get; private set; }
    public Guid? SpaceTypeId { get; private set; }
    public string? Room { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public TaskPriority Priority { get; private set; }
    public RequirementSource? SourceType { get; private set; }
    public Guid? MeetingId { get; private set; }
    public Guid? BriefId { get; private set; }
    public Guid? NoteId { get; private set; }
    public ApprovalState ApprovalStatus { get; private set; }
    public Guid? SupersedesItemId { get; private set; }
    public Guid? CreatedByEmployeeId { get; private set; }
    public Guid? CreatedByContactId { get; private set; }
    public Guid? ProjectTaskId { get; private set; }

    public bool IsLocked => ApprovalStatus is ApprovalState.Approved or ApprovalState.NotRequired;

    private CrmRequirementItem() { }

    public static Result<CrmRequirementItem> CreateByEmployee(
        Guid projectId, Guid periodId, Guid employeeId, RequirementCategory category, string title,
        TaskPriority priority, Guid? spaceTypeId = null, string? room = null, string? description = null,
        RequirementSource? sourceType = null, Guid? meetingId = null, Guid? briefId = null,
        Guid? noteId = null, Guid? projectTaskId = null)
    {
        if (!SourceMatches(sourceType, meetingId, briefId, noteId))
            return CrmItemErrors.InvalidSource;

        return new CrmRequirementItem
        {
            ProjectId = projectId,
            PeriodId = periodId,
            Category = category,
            Title = title,
            Priority = priority,
            SpaceTypeId = spaceTypeId,
            Room = room,
            Description = description,
            SourceType = sourceType,
            MeetingId = meetingId,
            BriefId = briefId,
            NoteId = noteId,
            CreatedByEmployeeId = employeeId,
            ProjectTaskId = projectTaskId,
            ApprovalStatus = ApprovalState.Draft,
        };
    }

    /// <summary>Requirements are the only artifact a client can author — trusted immediately, no
    /// source FKs.</summary>
    public static CrmRequirementItem CreateByClient(
        Guid projectId, Guid periodId, Guid contactId, RequirementCategory category, string title,
        TaskPriority priority, Guid? spaceTypeId = null, string? room = null, string? description = null)
    {
        return new CrmRequirementItem
        {
            ProjectId = projectId,
            PeriodId = periodId,
            Category = category,
            Title = title,
            Priority = priority,
            SpaceTypeId = spaceTypeId,
            Room = room,
            Description = description,
            SourceType = RequirementSource.ClientDirect,
            CreatedByContactId = contactId,
            ApprovalStatus = ApprovalState.NotRequired,
        };
    }

    /// <summary>Ledger rule: only a locked (Approved/NotRequired) item can be superseded.</summary>
    public static Result<CrmRequirementItem> CreateSuperseding(
        CrmRequirementItem original, Guid employeeId, RequirementCategory category, string title,
        TaskPriority priority, Guid? spaceTypeId = null, string? room = null, string? description = null,
        RequirementSource? sourceType = null, Guid? meetingId = null, Guid? briefId = null,
        Guid? noteId = null, Guid? projectTaskId = null)
    {
        if (!original.IsLocked)
            return CrmItemErrors.OnlyLockedSuperseded;

        var created = CreateByEmployee(
            original.ProjectId, original.PeriodId, employeeId, category, title, priority,
            spaceTypeId, room, description, sourceType, meetingId, briefId, noteId, projectTaskId);
        if (created.IsFailure) return created.Error;

        created.Value!.SupersedesItemId = original.Id;
        return created;
    }

    public Result Update(
        RequirementCategory category, string title, TaskPriority priority, Guid? spaceTypeId,
        string? room, string? description, RequirementSource? sourceType, Guid? meetingId,
        Guid? briefId, Guid? noteId, Guid? projectTaskId)
    {
        if (ApprovalStatus is not (ApprovalState.Draft or ApprovalState.Rejected))
            return CrmItemErrors.ItemLocked;

        if (!SourceMatches(sourceType, meetingId, briefId, noteId))
            return CrmItemErrors.InvalidSource;

        Category = category;
        Title = title;
        Priority = priority;
        SpaceTypeId = spaceTypeId;
        Room = room;
        Description = description;
        SourceType = sourceType;
        MeetingId = meetingId;
        BriefId = briefId;
        NoteId = noteId;
        ProjectTaskId = projectTaskId;
        return Result.Success();
    }

    public Result Send()
    {
        if (CreatedByEmployeeId is null)
            return CrmItemErrors.SendRequiresEmployeeAuthor;

        if (ApprovalStatus is not (ApprovalState.Draft or ApprovalState.Rejected))
            return CrmItemErrors.ItemLocked;

        ApprovalStatus = ApprovalState.PendingApproval;
        return Result.Success();
    }

    internal Result MarkApproved()
    {
        if (ApprovalStatus != ApprovalState.PendingApproval)
            return CrmItemErrors.NotPending;

        ApprovalStatus = ApprovalState.Approved;
        return Result.Success();
    }

    internal Result MarkRejected()
    {
        if (ApprovalStatus != ApprovalState.PendingApproval)
            return CrmItemErrors.NotPending;

        ApprovalStatus = ApprovalState.Rejected;
        return Result.Success();
    }

    /// <summary>Mirrors the DB check: exactly the FK named by <paramref name="sourceType"/> is set,
    /// none when null/ClientDirect.</summary>
    private static bool SourceMatches(RequirementSource? sourceType, Guid? meetingId, Guid? briefId, Guid? noteId)
    {
        var setCount = (meetingId is null ? 0 : 1) + (briefId is null ? 0 : 1) + (noteId is null ? 0 : 1);

        return sourceType switch
        {
            null => setCount == 0,
            RequirementSource.Meeting => meetingId is not null && setCount == 1,
            RequirementSource.Brief => briefId is not null && setCount == 1,
            RequirementSource.Note => noteId is not null && setCount == 1,
            RequirementSource.ClientDirect => setCount == 0,
            _ => false,
        };
    }
}
