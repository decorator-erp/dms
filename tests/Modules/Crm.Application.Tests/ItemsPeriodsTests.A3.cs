using Crm.Contracts;
using Crm.Contracts.Briefs;
using Crm.Contracts.Items;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;
using Xunit;
using RequirementCategory = Crm.Contracts.RequirementCategory;
using RequirementSource = Crm.Contracts.RequirementSource;

namespace Crm.Application.Tests;

/// <summary>A-3 (X-6): an office item belongs to its period's project, and every id it carries must
/// belong to that project too.</summary>
public sealed partial class ItemsPeriodsTests
{
    [Fact]
    public async Task A3_ABodyProjectId_ThatIsNotThePeriodsProject_Is400()
    {
        BriefTestProject p = await SeedAsync("a3-mismatch");
        BriefTestProject other = await SeedAsync("a3-mismatch-other");
        Guid periodId = await OpenPeriodAsync(p);

        Result<Guid> created = await CommandFor<CreateCrmRequirementItemCommand, Guid>(
            p.OwnerAppUserId, Item(p, periodId) with { ProjectId = other.ProjectId });

        AssertError(Plain(created), 400, "crm.items.ProjectMismatch");
    }

    [Fact]
    public async Task A3_ForeignTaskMeetingBriefOrNote_Is400_OnCreateUpdateAndSupersede()
    {
        BriefTestProject p = await SeedAsync("a3-refs");
        BriefTestProject other = await SeedAsync("a3-refs-other");
        Guid periodId = await OpenPeriodAsync(p);
        Guid draftId = await CreateItemAsync(p, periodId);
        Guid lockedId = await CreateLockedItemAsync(p, periodId);
        (Guid meetingId, Guid briefId, Guid noteId) = await SeedForeignSourcesAsync(other);

        (string Code, Guid? TaskId, RequirementSource? Source, Guid? MeetingId, Guid? BriefId, Guid? NoteId)[] cases =
        [
            ("crm.items.TaskProjectMismatch", other.TaskId, null, null, null, null),
            ("crm.items.MeetingProjectMismatch", null, RequirementSource.Meeting, meetingId, null, null),
            ("crm.items.BriefProjectMismatch", null, RequirementSource.Brief, null, briefId, null),
            ("crm.items.NoteProjectMismatch", null, RequirementSource.Note, null, null, noteId),
        ];

        foreach ((string code, Guid? taskId, RequirementSource? source, Guid? meeting, Guid? brief, Guid? note) in cases)
        {
            Result<Guid> created = await CommandFor<CreateCrmRequirementItemCommand, Guid>(p.OwnerAppUserId, Item(p, periodId) with
            {
                ProjectTaskId = taskId, SourceType = source, MeetingId = meeting, BriefId = brief, NoteId = note,
            });
            AssertError(Plain(created), 400, code);

            Result updated = await Command(p.OwnerAppUserId, new UpdateCrmRequirementItemCommand(
                draftId, RequirementCategory.Style, "Updated", RequirementPriority.Low, null, null, null,
                source, meeting, brief, note, taskId, p.OwnerAppUserId));
            AssertError(updated, 400, code);

            Result<Guid> superseded = await CommandFor<SupersedeCrmRequirementItemCommand, Guid>(p.OwnerAppUserId,
                new SupersedeCrmRequirementItemCommand(lockedId, p.EmployeeId, RequirementCategory.Style, "Fix", RequirementPriority.Low,
                    p.OwnerAppUserId, SourceType: source, MeetingId: meeting, BriefId: brief, NoteId: note, ProjectTaskId: taskId));
            AssertError(Plain(superseded), 400, code);
        }

        // Its own project's task is fine.
        Result<Guid> own = await CommandFor<CreateCrmRequirementItemCommand, Guid>(
            p.OwnerAppUserId, Item(p, periodId) with { ProjectTaskId = p.TaskId });
        Assert.True(own.IsSuccess, own.Error?.Description);
    }

    [Fact]
    public async Task A3_TheItemTakesItsProjectFromThePeriod_SoSendReachesThatProjectsClient()
    {
        BriefTestProject p = await SeedAsync("a3-send");
        Guid periodId = await OpenPeriodAsync(p);
        Guid itemId = await CreateItemAsync(p, periodId);

        Assert.True((await Command(p.OwnerAppUserId, new SendCrmRequirementItemCommand(itemId, p.OwnerAppUserId))).IsSuccess);

        CrmRequirementItem item = await LoadItemAsync(itemId);
        Assert.Equal(p.ProjectId, item.ProjectId);
        Assert.Equal(1, await CountApprovalsForClientAsync(itemId, p.ClientAppUserId));
    }

    /// <summary>A client-authored item: trusted at once (NotRequired), so it is locked and can be superseded.</summary>
    private async Task<Guid> CreateLockedItemAsync(BriefTestProject p, Guid periodId)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        CrmDbContext db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
        CrmContact contact = CrmContact.Create(p.ClientId, "Lock", "Contact", (Domain.ContactRole)1, isPrimary: false).Value!;
        db.CrmContacts.Add(contact);
        var item = CrmRequirementItem.CreateByClient(
            p.ProjectId, periodId, contact.Id, Domain.RequirementCategory.Style, "Client words", TaskPriority.Medium);
        db.CrmRequirementItems.Add(item);
        await db.SaveChangesAsync();
        return item.Id;
    }

    private async Task<(Guid MeetingId, Guid BriefId, Guid NoteId)> SeedForeignSourcesAsync(BriefTestProject other)
    {
        Result<Guid> brief = await CommandFor<CreateCrmDesignBriefCommand, Guid>(other.OwnerAppUserId,
            new CreateCrmDesignBriefCommand(other.ProjectId, other.EmployeeId, other.StageId,
                new Contracts.Briefs.CrmBriefHeadData((Contracts.UnitType)1, (Contracts.UnitCondition)1), other.OwnerAppUserId));
        Assert.True(brief.IsSuccess, brief.Error?.Description);

        await using AsyncServiceScope scope = fixture.CreateScope();
        CrmDbContext db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
        CrmMeeting meeting = CrmMeeting.Create(other.ProjectId, null, "Kickoff", Domain.MeetingType.InOffice, other.EmployeeId, null).Value!;
        var note = CrmClientNote.Create(other.ClientId, other.ProjectId, "Other client's note", other.EmployeeId);
        db.CrmMeetings.Add(meeting);
        db.CrmClientNotes.Add(note);
        await db.SaveChangesAsync();
        return (meeting.Id, brief.Value, note.Id);
    }

    private async Task<int> CountApprovalsForClientAsync(Guid itemId, string clientAppUserId)
    {
        await using var connection = new Npgsql.NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using Npgsql.NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT count(*) FROM approvals.approvals a
            JOIN approvals.approval_approvers r ON r.approval_id = a."Id"
            WHERE a."SubjectId" = @itemId AND r."ApproverUserId" = @user
            """;
        command.Parameters.AddWithValue("itemId", itemId);
        command.Parameters.AddWithValue("user", clientAppUserId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }
}
