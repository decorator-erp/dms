using System.Text;
using Approvals.Contracts;
using Approvals.Contracts.Approvals;
using BuildingBlocks.Events;
using BuildingBlocks.Messaging;
using Documents.Contracts.Documents;
using Documents.Infrastructure.Persistence;
using Hr.Contracts.Employees;
using Identity.Contracts.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Projects.Contracts;
using Projects.Contracts.Gates;
using Xunit;

namespace Documents.Application.Tests;

/// <summary>
/// G7-07's own verify block — "the phase's real proof task" per the plan: the
/// <see cref="ConditionType.Attachment"/>/<see cref="ConditionType.DocumentApproved"/> gate-creation
/// transition from <c>GateConditionErrors.UnsupportedConditionType</c> (Phase 2) to success, driven
/// through <c>OutboxProcessor.ProcessPendingAsync</c> rather than any direct in-process handler call —
/// the same "through the REAL delivery path" discipline <c>GateFlowTests</c> established for G5-08.
/// </summary>
[Collection(DocumentsApplicationCollection.Name)]
public sealed class FileFlowTests(DocumentsApplicationFixture fixture)
{
    private static Stream ContentStream(string text) => new MemoryStream(Encoding.UTF8.GetBytes(text));

    [Fact]
    public async Task AttachmentGate_OnTheUploadedTask_IsSatisfied_ThroughRealOutboxDelivery()
    {
        await using var scope = fixture.CreateScope();
        var sp = scope.ServiceProvider;
        var (projectId, ownerUserId, _, taskId) = await CreateProjectWithStageAndTaskAsync(sp);

        // Phase 2 returned GateConditionErrors.UnsupportedConditionType for Attachment - this is the
        // transition proof: it now succeeds.
        var createGate = sp.GetRequiredService<ICommandHandler<CreateTaskGateConditionCommand, Guid>>();
        var gateResult = await createGate.HandleAsync(new CreateTaskGateConditionCommand(
            projectId, taskId, ownerUserId, ConditionType.Attachment, GateTrigger.Unlock, "Needs a drawing"));
        Assert.True(gateResult.IsSuccess);
        var gateId = gateResult.Value;

        var upload = sp.GetRequiredService<ICommandHandler<UploadTaskDocumentCommand, Guid>>();
        var uploadResult = await upload.HandleAsync(new UploadTaskDocumentCommand(
            taskId, ContentStream("drawing"), "drawing.pdf", "application/pdf", 7, ownerUserId));
        Assert.True(uploadResult.IsSuccess);

        // Not a direct call to the gate-satisfaction logic - the row travels outbox -> OutboxProcessor ->
        // InboxDedupeDecorator -> DocumentUploadedIntegrationEventHandler, same discipline G5-08 proved.
        await fixture.OutboxProcessor.ProcessPendingAsync(CancellationToken.None);

        var getGates = sp.GetRequiredService<IQueryHandler<GetTaskGateConditionsQuery, IReadOnlyList<GateConditionResponse>>>();
        var gate = Assert.Single((await getGates.HandleAsync(new GetTaskGateConditionsQuery(projectId, taskId, ownerUserId))).Value!);
        Assert.Equal(gateId, gate.Id);
        Assert.Equal(GateStatus.Satisfied, gate.Status);
    }

    /// <summary>G7-02's correction 8, verified end-to-end: an <see cref="ConditionType.Attachment"/> gate
    /// on a STAGE is satisfied by a document uploaded to a TASK under that stage, resolved locally in
    /// <c>DocumentUploadedIntegrationEventHandler</c> via <c>ProjectTask.ProjectStageId</c> - no direct
    /// call, no cross-module round trip.</summary>
    [Fact]
    public async Task AttachmentGate_OnTheOwningStage_IsSatisfied_ByADocumentUploadedToATaskUnderIt()
    {
        await using var scope = fixture.CreateScope();
        var sp = scope.ServiceProvider;
        var (projectId, ownerUserId, stageId, taskId) = await CreateProjectWithStageAndTaskAsync(sp);

        var createGate = sp.GetRequiredService<ICommandHandler<CreateStageGateConditionCommand, Guid>>();
        var gateResult = await createGate.HandleAsync(new CreateStageGateConditionCommand(
            projectId, stageId, ownerUserId, ConditionType.Attachment, GateTrigger.Unlock, "Needs a stage doc"));
        Assert.True(gateResult.IsSuccess);

        var upload = sp.GetRequiredService<ICommandHandler<UploadTaskDocumentCommand, Guid>>();
        var uploadResult = await upload.HandleAsync(new UploadTaskDocumentCommand(
            taskId, ContentStream("reach-through"), "reach.pdf", "application/pdf", 13, ownerUserId));
        Assert.True(uploadResult.IsSuccess);

        await fixture.OutboxProcessor.ProcessPendingAsync(CancellationToken.None);

        var getGates = sp.GetRequiredService<IQueryHandler<GetStageGateConditionsQuery, IReadOnlyList<GateConditionResponse>>>();
        var gate = Assert.Single((await getGates.HandleAsync(new GetStageGateConditionsQuery(projectId, stageId, ownerUserId))).Value!);
        Assert.Equal(GateStatus.Satisfied, gate.Status);
    }

    /// <summary>
    /// The other half of the transition proof: a <see cref="ConditionType.DocumentApproved"/> gate now
    /// creates successfully (was <c>UnsupportedConditionType</c> in Phase 2), and requesting +
    /// approving a review on the referenced document's lineage satisfies it — through TWO real outbox
    /// hops (<c>ApprovalDecidedIntegrationEvent</c> -> Documents locks + re-enqueues
    /// <c>DocumentApprovedIntegrationEvent</c> -> Projects satisfies the gate), each its own cross-module
    /// integration event, never dispatched in the same transaction as a domain event.
    /// </summary>
    [Fact]
    public async Task DocumentApprovedGate_CreationSucceeds_AndIsSatisfiedWhenTheReviewIsApproved()
    {
        await using var scope = fixture.CreateScope();
        var sp = scope.ServiceProvider;
        var (projectId, ownerUserId, _, taskId) = await CreateProjectWithStageAndTaskAsync(sp);
        var (_, approverEmployeeId) = await RegisterEmployeeAsync(sp, "doc-approver");

        var upload = sp.GetRequiredService<ICommandHandler<UploadTaskDocumentCommand, Guid>>();
        var documentId = (await upload.HandleAsync(new UploadTaskDocumentCommand(
            taskId, ContentStream("spec"), "spec.pdf", "application/pdf", 4, ownerUserId))).Value;

        var getDocument = sp.GetRequiredService<IQueryHandler<GetProjectDocumentByIdQuery, ProjectDocumentResponse>>();
        var rootDocumentId = (await getDocument.HandleAsync(new GetProjectDocumentByIdQuery(documentId))).Value!.RootDocumentId;

        var createGate = sp.GetRequiredService<ICommandHandler<CreateTaskGateConditionCommand, Guid>>();
        var gateResult = await createGate.HandleAsync(new CreateTaskGateConditionCommand(
            projectId, taskId, ownerUserId, ConditionType.DocumentApproved, GateTrigger.Unlock, "Needs client sign-off",
            ReferencedDocumentId: rootDocumentId));
        Assert.True(gateResult.IsSuccess);

        var requestReview = sp.GetRequiredService<ICommandHandler<RequestDocumentReviewCommand, Guid>>();
        var reviewResult = await requestReview.HandleAsync(
            new RequestDocumentReviewCommand(documentId, approverEmployeeId, ownerUserId));
        Assert.True(reviewResult.IsSuccess);
        var approvalId = reviewResult.Value;

        var currentUser = sp.GetRequiredService<TestCurrentUser>();
        var approverAppUserId = (await sp.GetRequiredService<Hr.Contracts.IHrDirectory>().GetAsync(approverEmployeeId, CancellationToken.None))!.AppUserId;
        currentUser.UserId = approverAppUserId;

        var decide = sp.GetRequiredService<ICommandHandler<DecideApprovalCommand>>();
        Assert.True((await decide.HandleAsync(new DecideApprovalCommand(approvalId, ApprovalDecision.Approved, Comment: null))).IsSuccess);

        // Hop 1: ApprovalDecidedIntegrationEvent -> Documents.ApprovalDecidedIntegrationEventHandler locks
        // the document and enqueues DocumentApprovedIntegrationEvent in the SAME SaveChangesAsync.
        await fixture.OutboxProcessor.ProcessPendingAsync(CancellationToken.None);

        var afterLock = await getDocument.HandleAsync(new GetProjectDocumentByIdQuery(documentId));
        Assert.True(afterLock.Value!.IsLocked);

        // Hop 2: that newly-enqueued row wasn't in hop 1's pending snapshot - a second pass delivers it.
        await fixture.OutboxProcessor.ProcessPendingAsync(CancellationToken.None);

        var getGates = sp.GetRequiredService<IQueryHandler<GetTaskGateConditionsQuery, IReadOnlyList<GateConditionResponse>>>();
        var gate = Assert.Single((await getGates.HandleAsync(new GetTaskGateConditionsQuery(projectId, taskId, ownerUserId))).Value!);
        Assert.Equal(GateStatus.Satisfied, gate.Status);
    }

    /// <summary>Proves the subject-type filter both ways round: reuses a REAL task gate's own id as a
    /// FAKE <c>ApprovalDecidedIntegrationEvent</c>'s SubjectId, but tags it
    /// <see cref="ApprovalSubjectType.ProjectDocument"/> - the same id-reuse technique
    /// <c>GateFlowTests.ApprovalDecidedEvent_ForADesignBriefSubject_DoesNotTouchAnyGate</c> already
    /// established. If Projects' own <c>ApprovalDecidedIntegrationEventHandler</c> subject-type filter
    /// didn't hold, this gate would flip to Satisfied even though nothing about it was ever a document
    /// review.</summary>
    [Fact]
    public async Task ProjectDocumentSubjectDecision_NeverReachesProjectsOwnGateConditionSubscriber()
    {
        await using var scope = fixture.CreateScope();
        var sp = scope.ServiceProvider;
        var (projectId, ownerUserId, _, taskId) = await CreateProjectWithStageAndTaskAsync(sp);

        var createGate = sp.GetRequiredService<ICommandHandler<CreateTaskGateConditionCommand, Guid>>();
        var gateId = (await createGate.HandleAsync(new CreateTaskGateConditionCommand(
            projectId, taskId, ownerUserId, ConditionType.ManualCheck, GateTrigger.Unlock, "Manual check"))).Value;

        var eventBus = sp.GetRequiredService<IEventBus>();
        await eventBus.PublishAsync(
            new ApprovalDecidedIntegrationEvent(
                Guid.NewGuid(), Guid.NewGuid(), ApprovalSubjectType.ProjectDocument, gateId,
                ApprovalDecision.Approved, ownerUserId, DateTimeOffset.UtcNow),
            CancellationToken.None);

        var getGates = sp.GetRequiredService<IQueryHandler<GetTaskGateConditionsQuery, IReadOnlyList<GateConditionResponse>>>();
        var gate = Assert.Single((await getGates.HandleAsync(new GetTaskGateConditionsQuery(projectId, taskId, ownerUserId))).Value!);
        Assert.Equal(GateStatus.Pending, gate.Status);
    }

    /// <summary>G7-02's correction 4, end-to-end: delete keeps the blob (unlike the backend's hard-delete)
    /// and removes the row from a project list query.</summary>
    [Fact]
    public async Task Delete_SoftDeletesAndExcludesFromListQuery_ButKeepsTheBlobOnDisk()
    {
        await using var scope = fixture.CreateScope();
        var sp = scope.ServiceProvider;
        var (projectId, ownerUserId, _, taskId) = await CreateProjectWithStageAndTaskAsync(sp);

        var upload = sp.GetRequiredService<ICommandHandler<UploadTaskDocumentCommand, Guid>>();
        var documentId = (await upload.HandleAsync(new UploadTaskDocumentCommand(
            taskId, ContentStream("keep-me"), "keep.pdf", "application/pdf", 8, ownerUserId))).Value;

        var db = sp.GetRequiredService<DocumentsDbContext>();
        var storagePath = await db.ProjectDocuments.AsNoTracking()
            .Where(d => d.Id == documentId).Select(d => d.StoragePath).SingleAsync();

        var delete = sp.GetRequiredService<ICommandHandler<DeleteProjectDocumentCommand>>();
        Assert.True((await delete.HandleAsync(new DeleteProjectDocumentCommand(documentId))).IsSuccess);

        var getByProject = sp.GetRequiredService<IQueryHandler<GetProjectDocumentsByProjectQuery, IReadOnlyList<ProjectDocumentResponse>>>();
        var afterDelete = (await getByProject.HandleAsync(new GetProjectDocumentsByProjectQuery(projectId))).Value!;
        Assert.DoesNotContain(afterDelete, d => d.Id == documentId);

        var fileStorage = sp.GetRequiredService<Documents.Contracts.IFileStorageService>();
        await using var blob = await fileStorage.OpenReadAsync(storagePath, CancellationToken.None);
        Assert.NotNull(blob);
    }

    /// <summary>G7-02's correction 4, the other half: a soft-deleted document in the middle of a lineage
    /// does not break resolving the true latest revision - <c>UploadDocumentRevisionCommandHandler</c>'s
    /// lookup deliberately does not filter on <c>IsActive</c> (only the list/get-by-id queries do).</summary>
    [Fact]
    public async Task SoftDeletedRevision_DoesNotBreakLineageResolution_ForTheNextRevision()
    {
        await using var scope = fixture.CreateScope();
        var sp = scope.ServiceProvider;
        var (_, ownerUserId, _, taskId) = await CreateProjectWithStageAndTaskAsync(sp);

        var upload = sp.GetRequiredService<ICommandHandler<UploadTaskDocumentCommand, Guid>>();
        var v1Id = (await upload.HandleAsync(new UploadTaskDocumentCommand(
            taskId, ContentStream("v1"), "doc.pdf", "application/pdf", 2, ownerUserId))).Value;

        var delete = sp.GetRequiredService<ICommandHandler<DeleteProjectDocumentCommand>>();
        Assert.True((await delete.HandleAsync(new DeleteProjectDocumentCommand(v1Id))).IsSuccess);

        var getById = sp.GetRequiredService<IQueryHandler<GetProjectDocumentByIdQuery, ProjectDocumentResponse>>();
        Assert.True((await getById.HandleAsync(new GetProjectDocumentByIdQuery(v1Id))).IsFailure);

        var uploadRevision = sp.GetRequiredService<ICommandHandler<UploadDocumentRevisionCommand, Guid>>();
        var revisionResult = await uploadRevision.HandleAsync(new UploadDocumentRevisionCommand(
            v1Id, ContentStream("v2"), "doc.pdf", "application/pdf", 2, ownerUserId));
        Assert.True(revisionResult.IsSuccess);

        var db = sp.GetRequiredService<DocumentsDbContext>();
        var v1RootId = await db.ProjectDocuments.AsNoTracking().Where(d => d.Id == v1Id).Select(d => d.RootDocumentId).SingleAsync();
        var v2 = await db.ProjectDocuments.AsNoTracking().SingleAsync(d => d.Id == revisionResult.Value);
        Assert.Equal(v1RootId, v2.RootDocumentId);
        Assert.Equal(2, v2.Version);
    }

    private static async Task<(Guid ProjectId, string OwnerUserId, Guid StageId, Guid TaskId)> CreateProjectWithStageAndTaskAsync(
        IServiceProvider sp)
    {
        var (projectId, stageId, taskId) = await FolderFlowTests.CreateProjectWithStageAndTaskAsync(sp);
        var currentUser = sp.GetRequiredService<TestCurrentUser>();
        return (projectId, currentUser.UserId!, stageId, taskId);
    }

    private static async Task<(string UserId, Guid EmployeeId)> RegisterEmployeeAsync(IServiceProvider sp, string label)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var register = sp.GetRequiredService<ICommandHandler<RegisterCommand, string>>();
        var registerResult = await register.HandleAsync(new RegisterCommand(
            new RegisterRequest($"{label}-{suffix}@example.com", "P@ssw0rd123!", "Test", label, null, "employee")));
        Assert.True(registerResult.IsSuccess);
        var appUserId = registerResult.Value!;

        var createEmployee = sp.GetRequiredService<ICommandHandler<CreateEmployeeCommand, Guid>>();
        var createResult = await createEmployee.HandleAsync(
            new CreateEmployeeCommand(appUserId, "Reviewer", DateOnly.FromDateTime(DateTime.UtcNow), "FullTime"));
        Assert.True(createResult.IsSuccess);

        return (appUserId, createResult.Value);
    }
}
