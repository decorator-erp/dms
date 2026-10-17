using System.Text;
using Approvals.Contracts;
using Approvals.Contracts.Approvals;
using BuildingBlocks.Events;
using BuildingBlocks.Messaging;
using Documents.Contracts;
using Documents.Contracts.Documents;
using Documents.Contracts.FileTemplates;
using Documents.Domain;
using Documents.Infrastructure.Persistence;
using Hr.Contracts.Employees;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Projects.Contracts;
using Projects.Contracts.Gates;
using SharedKernel;
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
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        (Guid projectId, string? ownerUserId, Guid _, Guid taskId) = await CreateProjectWithStageAndTaskAsync(sp);

        // Phase 2 returned GateConditionErrors.UnsupportedConditionType for Attachment - this is the
        // transition proof: it now succeeds.
        ICommandHandler<CreateTaskGateConditionCommand, Guid> createGate = sp.GetRequiredService<ICommandHandler<CreateTaskGateConditionCommand, Guid>>();
        Result<Guid> gateResult = await createGate.HandleAsync(new CreateTaskGateConditionCommand(
            projectId, taskId, ownerUserId, ConditionType.Attachment, GateTrigger.Unlock, "Needs a drawing"));
        Assert.True(gateResult.IsSuccess);
        Guid gateId = gateResult.Value;

        ICommandHandler<UploadTaskDocumentCommand, Guid> upload = sp.GetRequiredService<ICommandHandler<UploadTaskDocumentCommand, Guid>>();
        Result<Guid> uploadResult = await upload.HandleAsync(new UploadTaskDocumentCommand(
            taskId, ContentStream("drawing"), "drawing.pdf", "application/pdf", 7, ownerUserId));
        Assert.True(uploadResult.IsSuccess);

        // Not a direct call to the gate-satisfaction logic - the row travels outbox -> OutboxProcessor ->
        // InboxDedupeDecorator -> DocumentUploadedIntegrationEventHandler, same discipline G5-08 proved.
        await fixture.OutboxProcessor.ProcessPendingAsync(CancellationToken.None);

        IQueryHandler<GetTaskGateConditionsQuery, IReadOnlyList<GateConditionResponse>> getGates = sp.GetRequiredService<IQueryHandler<GetTaskGateConditionsQuery, IReadOnlyList<GateConditionResponse>>>();
        GateConditionResponse gate = Assert.Single((await getGates.HandleAsync(new GetTaskGateConditionsQuery(projectId, taskId, ownerUserId))).Value!);
        Assert.Equal(gateId, gate.Id);
        Assert.Equal(GateStatus.Satisfied, gate.Status);
    }

    /// <summary>
    /// G7-02's correction 8, gate half, verified end-to-end: an <see cref="ConditionType.Attachment"/>
    /// gate on a STAGE is satisfied by a document uploaded to a TASK under that stage.
    ///
    /// <para><b>What this proves changed at E1-01, and the comment is corrected rather than left
    /// stale.</b> Before E1-01 the event carried <c>ProjectStageId = null</c> and
    /// <c>DocumentUploadedIntegrationEventHandler</c> resolved the owning stage itself via its
    /// <c>owningStageId ??=</c> lookup — which is what this comment used to claim it proved. E1-01
    /// denormalizes the stage at upload, so the event now arrives WITH it and that fallback no longer
    /// fires on this path. This test therefore proves the DENORMALIZED route today; the fallback keeps
    /// its own coverage in
    /// <see cref="AttachmentGate_OnTheOwningStage_IsSatisfied_ByAnEventCarryingNoStage"/>.
    /// Left uncorrected, this test would have stayed green while silently testing something else —
    /// the same "green test, evaporated coverage" failure mode that let E1 ship.</para>
    /// </summary>
    [Fact]
    public async Task AttachmentGate_OnTheOwningStage_IsSatisfied_ByADocumentUploadedToATaskUnderIt()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        (Guid projectId, string? ownerUserId, Guid stageId, Guid taskId) = await CreateProjectWithStageAndTaskAsync(sp);

        ICommandHandler<CreateStageGateConditionCommand, Guid> createGate = sp.GetRequiredService<ICommandHandler<CreateStageGateConditionCommand, Guid>>();
        Result<Guid> gateResult = await createGate.HandleAsync(new CreateStageGateConditionCommand(
            projectId, stageId, ownerUserId, ConditionType.Attachment, GateTrigger.Unlock, "Needs a stage doc"));
        Assert.True(gateResult.IsSuccess);

        ICommandHandler<UploadTaskDocumentCommand, Guid> upload = sp.GetRequiredService<ICommandHandler<UploadTaskDocumentCommand, Guid>>();
        Result<Guid> uploadResult = await upload.HandleAsync(new UploadTaskDocumentCommand(
            taskId, ContentStream("reach-through"), "reach.pdf", "application/pdf", 13, ownerUserId));
        Assert.True(uploadResult.IsSuccess);

        await fixture.OutboxProcessor.ProcessPendingAsync(CancellationToken.None);

        IQueryHandler<GetStageGateConditionsQuery, IReadOnlyList<GateConditionResponse>> getGates = sp.GetRequiredService<IQueryHandler<GetStageGateConditionsQuery, IReadOnlyList<GateConditionResponse>>>();
        GateConditionResponse gate = Assert.Single((await getGates.HandleAsync(new GetStageGateConditionsQuery(projectId, stageId, ownerUserId))).Value!);
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
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        (Guid projectId, string? ownerUserId, Guid _, Guid taskId) = await CreateProjectWithStageAndTaskAsync(sp);
        (string _, Guid approverEmployeeId) = await RegisterEmployeeAsync(sp, "doc-approver");

        ICommandHandler<UploadTaskDocumentCommand, Guid> upload = sp.GetRequiredService<ICommandHandler<UploadTaskDocumentCommand, Guid>>();
        Guid documentId = (await upload.HandleAsync(new UploadTaskDocumentCommand(
            taskId, ContentStream("spec"), "spec.pdf", "application/pdf", 4, ownerUserId))).Value;

        IQueryHandler<GetProjectDocumentByIdQuery, ProjectDocumentResponse> getDocument = sp.GetRequiredService<IQueryHandler<GetProjectDocumentByIdQuery, ProjectDocumentResponse>>();
        Guid rootDocumentId = (await getDocument.HandleAsync(new GetProjectDocumentByIdQuery(documentId))).Value!.RootDocumentId;

        ICommandHandler<CreateTaskGateConditionCommand, Guid> createGate = sp.GetRequiredService<ICommandHandler<CreateTaskGateConditionCommand, Guid>>();
        Result<Guid> gateResult = await createGate.HandleAsync(new CreateTaskGateConditionCommand(
            projectId, taskId, ownerUserId, ConditionType.DocumentApproved, GateTrigger.Unlock, "Needs client sign-off",
            ReferencedDocumentId: rootDocumentId));
        Assert.True(gateResult.IsSuccess);

        ICommandHandler<RequestDocumentReviewCommand, Guid> requestReview = sp.GetRequiredService<ICommandHandler<RequestDocumentReviewCommand, Guid>>();
        Result<Guid> reviewResult = await requestReview.HandleAsync(
            new RequestDocumentReviewCommand(documentId, approverEmployeeId, ownerUserId));
        Assert.True(reviewResult.IsSuccess);
        Guid approvalId = reviewResult.Value;

        TestCurrentUser currentUser = sp.GetRequiredService<TestCurrentUser>();
        string approverAppUserId = (await sp.GetRequiredService<Hr.Contracts.IHrDirectory>().GetAsync(approverEmployeeId, CancellationToken.None))!.AppUserId;
        currentUser.UserId = approverAppUserId;

        ICommandHandler<DecideApprovalCommand> decide = sp.GetRequiredService<ICommandHandler<DecideApprovalCommand>>();
        Assert.True((await decide.HandleAsync(new DecideApprovalCommand(approvalId, ApprovalDecision.Approved, Comment: null))).IsSuccess);

        // Hop 1: ApprovalDecidedIntegrationEvent -> Documents.ApprovalDecidedIntegrationEventHandler locks
        // the document and enqueues DocumentApprovedIntegrationEvent in the SAME SaveChangesAsync.
        await fixture.OutboxProcessor.ProcessPendingAsync(CancellationToken.None);

        Result<ProjectDocumentResponse> afterLock = await getDocument.HandleAsync(new GetProjectDocumentByIdQuery(documentId));
        Assert.True(afterLock.Value!.IsLocked);

        // Hop 2: that newly-enqueued row wasn't in hop 1's pending snapshot - a second pass delivers it.
        await fixture.OutboxProcessor.ProcessPendingAsync(CancellationToken.None);

        IQueryHandler<GetTaskGateConditionsQuery, IReadOnlyList<GateConditionResponse>> getGates = sp.GetRequiredService<IQueryHandler<GetTaskGateConditionsQuery, IReadOnlyList<GateConditionResponse>>>();
        GateConditionResponse gate = Assert.Single((await getGates.HandleAsync(new GetTaskGateConditionsQuery(projectId, taskId, ownerUserId))).Value!);
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
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        (Guid projectId, string? ownerUserId, Guid _, Guid taskId) = await CreateProjectWithStageAndTaskAsync(sp);

        ICommandHandler<CreateTaskGateConditionCommand, Guid> createGate = sp.GetRequiredService<ICommandHandler<CreateTaskGateConditionCommand, Guid>>();
        Guid gateId = (await createGate.HandleAsync(new CreateTaskGateConditionCommand(
            projectId, taskId, ownerUserId, ConditionType.ManualCheck, GateTrigger.Unlock, "Manual check"))).Value;

        IEventBus eventBus = sp.GetRequiredService<IEventBus>();
        await eventBus.PublishAsync(
            new ApprovalDecidedIntegrationEvent(
                Guid.NewGuid(), Guid.NewGuid(), ApprovalSubjectType.ProjectDocument, gateId,
                ApprovalDecision.Approved, ownerUserId, DateTimeOffset.UtcNow),
            CancellationToken.None);

        IQueryHandler<GetTaskGateConditionsQuery, IReadOnlyList<GateConditionResponse>> getGates = sp.GetRequiredService<IQueryHandler<GetTaskGateConditionsQuery, IReadOnlyList<GateConditionResponse>>>();
        GateConditionResponse gate = Assert.Single((await getGates.HandleAsync(new GetTaskGateConditionsQuery(projectId, taskId, ownerUserId))).Value!);
        Assert.Equal(GateStatus.Pending, gate.Status);
    }

    /// <summary>G7-02's correction 4, end-to-end: delete keeps the blob (unlike the backend's hard-delete)
    /// and removes the row from a project list query.</summary>
    [Fact]
    public async Task Delete_SoftDeletesAndExcludesFromListQuery_ButKeepsTheBlobOnDisk()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        (Guid projectId, string? ownerUserId, Guid _, Guid taskId) = await CreateProjectWithStageAndTaskAsync(sp);

        ICommandHandler<UploadTaskDocumentCommand, Guid> upload = sp.GetRequiredService<ICommandHandler<UploadTaskDocumentCommand, Guid>>();
        Guid documentId = (await upload.HandleAsync(new UploadTaskDocumentCommand(
            taskId, ContentStream("keep-me"), "keep.pdf", "application/pdf", 8, ownerUserId))).Value;

        DocumentsDbContext db = sp.GetRequiredService<DocumentsDbContext>();
        string storagePath = await db.ProjectDocuments.AsNoTracking()
            .Where(d => d.Id == documentId).Select(d => d.StoragePath).SingleAsync();

        IQueryHandler<GetProjectDocumentsByProjectQuery, IReadOnlyList<ProjectDocumentResponse>> getByProject = sp.GetRequiredService<IQueryHandler<GetProjectDocumentsByProjectQuery, IReadOnlyList<ProjectDocumentResponse>>>();

        // PC-03 liveness, as a before/after pair: the list query must be shown to return this document
        // while it exists, or its later absence is equally well explained by a query that lists nothing
        // for this project at all.
        IReadOnlyList<ProjectDocumentResponse> beforeDelete = (await getByProject.HandleAsync(new GetProjectDocumentsByProjectQuery(projectId))).Value!;
        Assert.Contains(beforeDelete, d => d.Id == documentId);

        ICommandHandler<DeleteProjectDocumentCommand> delete = sp.GetRequiredService<ICommandHandler<DeleteProjectDocumentCommand>>();
        Assert.True((await delete.HandleAsync(new DeleteProjectDocumentCommand(documentId))).IsSuccess);

        IReadOnlyList<ProjectDocumentResponse> afterDelete = (await getByProject.HandleAsync(new GetProjectDocumentsByProjectQuery(projectId))).Value!;
        Assert.DoesNotContain(afterDelete, d => d.Id == documentId);

        IFileStorageService fileStorage = sp.GetRequiredService<Documents.Contracts.IFileStorageService>();
        await using Stream? blob = await fileStorage.OpenReadAsync(storagePath, CancellationToken.None);
        Assert.NotNull(blob);
    }

    /// <summary>G7-02's correction 4, the other half: a soft-deleted document in the middle of a lineage
    /// does not break resolving the true latest revision - <c>UploadDocumentRevisionCommandHandler</c>'s
    /// lookup deliberately does not filter on <c>IsActive</c> (only the list/get-by-id queries do).</summary>
    [Fact]
    public async Task SoftDeletedRevision_DoesNotBreakLineageResolution_ForTheNextRevision()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        (Guid _, string? ownerUserId, Guid _, Guid taskId) = await CreateProjectWithStageAndTaskAsync(sp);

        ICommandHandler<UploadTaskDocumentCommand, Guid> upload = sp.GetRequiredService<ICommandHandler<UploadTaskDocumentCommand, Guid>>();
        Guid v1Id = (await upload.HandleAsync(new UploadTaskDocumentCommand(
            taskId, ContentStream("v1"), "doc.pdf", "application/pdf", 2, ownerUserId))).Value;

        ICommandHandler<DeleteProjectDocumentCommand> delete = sp.GetRequiredService<ICommandHandler<DeleteProjectDocumentCommand>>();
        Assert.True((await delete.HandleAsync(new DeleteProjectDocumentCommand(v1Id))).IsSuccess);

        IQueryHandler<GetProjectDocumentByIdQuery, ProjectDocumentResponse> getById = sp.GetRequiredService<IQueryHandler<GetProjectDocumentByIdQuery, ProjectDocumentResponse>>();
        Assert.True((await getById.HandleAsync(new GetProjectDocumentByIdQuery(v1Id))).IsFailure);

        ICommandHandler<UploadDocumentRevisionCommand, Guid> uploadRevision = sp.GetRequiredService<ICommandHandler<UploadDocumentRevisionCommand, Guid>>();
        Result<Guid> revisionResult = await uploadRevision.HandleAsync(new UploadDocumentRevisionCommand(
            v1Id, ContentStream("v2"), "doc.pdf", "application/pdf", 2, ownerUserId));
        Assert.True(revisionResult.IsSuccess);

        DocumentsDbContext db = sp.GetRequiredService<DocumentsDbContext>();
        Guid v1RootId = await db.ProjectDocuments.AsNoTracking().Where(d => d.Id == v1Id).Select(d => d.RootDocumentId).SingleAsync();
        ProjectDocument v2 = await db.ProjectDocuments.AsNoTracking().SingleAsync(d => d.Id == revisionResult.Value);
        Assert.Equal(v1RootId, v2.RootDocumentId);
        Assert.Equal(2, v2.Version);
    }

    /// <summary>
    /// Keeps <c>DocumentUploadedIntegrationEventHandler</c>'s <c>owningStageId ??=</c> fallback genuinely
    /// covered after E1-01 made the normal upload path stop exercising it (see the test above). Publishes
    /// a synthetic event carrying <c>ProjectStageId: null</c> with only a task id — the exact shape every
    /// pre-E1-01 row produced, and the shape any future producer that forgets to denormalize would
    /// produce — straight through <see cref="IEventBus"/>, the same synthetic-event technique
    /// <see cref="ProjectDocumentSubjectDecision_NeverReachesProjectsOwnGateConditionSubscriber"/> uses.
    /// Without this, the fallback would be untested code that a later reader deletes as dead, silently
    /// reintroducing E1 for exactly those producers.
    /// </summary>
    [Fact]
    public async Task AttachmentGate_OnTheOwningStage_IsSatisfied_ByAnEventCarryingNoStage()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        (Guid projectId, string? ownerUserId, Guid stageId, Guid taskId) = await CreateProjectWithStageAndTaskAsync(sp);

        ICommandHandler<CreateStageGateConditionCommand, Guid> createGate = sp.GetRequiredService<ICommandHandler<CreateStageGateConditionCommand, Guid>>();
        Result<Guid> gateResult = await createGate.HandleAsync(new CreateStageGateConditionCommand(
            projectId, stageId, ownerUserId, ConditionType.Attachment, GateTrigger.Unlock, "Needs a stage doc"));
        Assert.True(gateResult.IsSuccess);

        // ProjectStageId: null with a real ProjectTaskId - the pre-E1-01 event shape. Only those two
        // fields matter to the handler; the rest are inert for this path.
        IEventBus eventBus = sp.GetRequiredService<IEventBus>();
        await eventBus.PublishAsync(
            new Documents.Contracts.DocumentUploadedIntegrationEvent(
                Guid.NewGuid(), Guid.NewGuid(), projectId, ProjectStageId: null, ProjectTaskId: taskId,
                FolderId: null, "legacy.pdf", 1, Guid.NewGuid(), DateTimeOffset.UtcNow),
            CancellationToken.None);

        IQueryHandler<GetStageGateConditionsQuery, IReadOnlyList<GateConditionResponse>> getGates = sp.GetRequiredService<IQueryHandler<GetStageGateConditionsQuery, IReadOnlyList<GateConditionResponse>>>();
        GateConditionResponse gate = Assert.Single((await getGates.HandleAsync(new GetStageGateConditionsQuery(projectId, stageId, ownerUserId))).Value!);
        Assert.Equal(GateStatus.Satisfied, gate.Status);
    }

    /// <summary>
    /// E1-01, the query half of G7-02's correction 8 — the half that never shipped at G7-07. A document
    /// uploaded to a TASK must be visible to a STAGE-scoped read of the stage that owns that task, or
    /// (correction 8's own words) "the <c>Attachment</c> gate on a stage will read as unsatisfied when a
    /// task under it has the file." The GATE half was built on Projects' subscriber side and works; this
    /// read half was left direct-scope-only, and neither stage-read path had any test at all, which is
    /// exactly why nothing caught it.
    ///
    /// <para>These three tests are the red-first proof: each fails on its own assertion (an empty result
    /// set) before <c>UploadTaskDocumentCommandHandler</c>/<c>ApplyFileTemplateToTaskCommandHandler</c>
    /// denormalize <c>ProjectStageId</c>, and passes after. The pre-existing gate test above cannot serve
    /// that purpose — it passes either way, since Projects resolves the owning stage independently.</para>
    /// </summary>
    [Fact]
    public async Task StageRead_ViaQueryHandler_IncludesADocumentUploadedToATaskUnderThatStage()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        (Guid _, string? ownerUserId, Guid stageId, Guid taskId) = await CreateProjectWithStageAndTaskAsync(sp);

        ICommandHandler<UploadTaskDocumentCommand, Guid> upload = sp.GetRequiredService<ICommandHandler<UploadTaskDocumentCommand, Guid>>();
        Result<Guid> uploadResult = await upload.HandleAsync(new UploadTaskDocumentCommand(
            taskId, ContentStream("task doc"), "task-doc.pdf", "application/pdf", 8, ownerUserId));
        Assert.True(uploadResult.IsSuccess);
        Guid documentId = uploadResult.Value;

        IQueryHandler<GetProjectDocumentsByStageQuery, IReadOnlyList<ProjectDocumentResponse>> getByStage = sp.GetRequiredService<IQueryHandler<GetProjectDocumentsByStageQuery, IReadOnlyList<ProjectDocumentResponse>>>();
        IReadOnlyList<ProjectDocumentResponse> stageDocuments = (await getByStage.HandleAsync(new GetProjectDocumentsByStageQuery(stageId))).Value!;

        ProjectDocumentResponse document = Assert.Single(stageDocuments, d => d.Id == documentId);
        Assert.Equal(stageId, document.ProjectStageId);
        Assert.Equal(taskId, document.ProjectTaskId);
    }

    /// <summary>The same reach-through through the CROSS-MODULE contract surface rather than this module's
    /// own query handler — correction 8 named <c>IDocumentQueries.ForStageAsync</c> by name, so covering
    /// only the internal query handler would leave the interface the correction actually cited untested.</summary>
    [Fact]
    public async Task StageRead_ViaDocumentQueries_IncludesADocumentUploadedToATaskUnderThatStage()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        (Guid _, string? ownerUserId, Guid stageId, Guid taskId) = await CreateProjectWithStageAndTaskAsync(sp);

        ICommandHandler<UploadTaskDocumentCommand, Guid> upload = sp.GetRequiredService<ICommandHandler<UploadTaskDocumentCommand, Guid>>();
        Result<Guid> uploadResult = await upload.HandleAsync(new UploadTaskDocumentCommand(
            taskId, ContentStream("contract read"), "contract-read.pdf", "application/pdf", 13, ownerUserId));
        Assert.True(uploadResult.IsSuccess);
        Guid documentId = uploadResult.Value;

        IDocumentQueries documentQueries = sp.GetRequiredService<Documents.Contracts.IDocumentQueries>();
        IReadOnlyList<DocumentSummary> stageDocuments = await documentQueries.ForStageAsync(stageId, CancellationToken.None);

        DocumentSummary document = Assert.Single(stageDocuments, d => d.Id == documentId);
        Assert.Equal(stageId, document.ProjectStageId);
        Assert.Equal(taskId, document.ProjectTaskId);
    }

    /// <summary><c>ApplyFileTemplateToTaskCommand</c> is the OTHER task-scoped construction path, and it
    /// carried the identical omission. It had zero test coverage of any kind before E1-01 — fixing its
    /// one-liner without covering it would have shipped exactly the sort of unverified change this task
    /// exists to correct.</summary>
    [Fact]
    public async Task StageRead_ViaAppliedTemplate_IncludesTheDocumentTheTemplateCreated()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        (Guid _, string? ownerUserId, Guid stageId, Guid taskId) = await CreateProjectWithStageAndTaskAsync(sp);

        ICommandHandler<CreateFileTemplateCommand, Guid> createTemplate = sp.GetRequiredService<ICommandHandler<CreateFileTemplateCommand, Guid>>();
        Result<Guid> templateResult = await createTemplate.HandleAsync(new CreateFileTemplateCommand(
            $"Cover Sheet {Guid.NewGuid():N}", nameof(Documents.Domain.FileTemplateCategory.Document),
            ContentStream("template body"), "cover.docx", "application/vnd.openxmlformats", 13));
        Assert.True(templateResult.IsSuccess);

        ICommandHandler<ApplyFileTemplateToTaskCommand, Guid> applyTemplate = sp.GetRequiredService<ICommandHandler<ApplyFileTemplateToTaskCommand, Guid>>();
        Result<Guid> applyResult = await applyTemplate.HandleAsync(
            new ApplyFileTemplateToTaskCommand(taskId, templateResult.Value, ownerUserId));
        Assert.True(applyResult.IsSuccess);

        IQueryHandler<GetProjectDocumentsByStageQuery, IReadOnlyList<ProjectDocumentResponse>> getByStage = sp.GetRequiredService<IQueryHandler<GetProjectDocumentsByStageQuery, IReadOnlyList<ProjectDocumentResponse>>>();
        IReadOnlyList<ProjectDocumentResponse> stageDocuments = (await getByStage.HandleAsync(new GetProjectDocumentsByStageQuery(stageId))).Value!;

        ProjectDocumentResponse document = Assert.Single(stageDocuments, d => d.Id == applyResult.Value);
        Assert.Equal(stageId, document.ProjectStageId);
        Assert.Equal(taskId, document.ProjectTaskId);
    }

    private static async Task<(Guid ProjectId, string OwnerUserId, Guid StageId, Guid TaskId)> CreateProjectWithStageAndTaskAsync(
        IServiceProvider sp)
    {
        (Guid projectId, Guid stageId, Guid taskId) = await FolderFlowTests.CreateProjectWithStageAndTaskAsync(sp);
        TestCurrentUser currentUser = sp.GetRequiredService<TestCurrentUser>();
        return (projectId, currentUser.UserId!, stageId, taskId);
    }

    private static async Task<(string UserId, Guid EmployeeId)> RegisterEmployeeAsync(IServiceProvider sp, string label)
    {
        string suffix = Guid.NewGuid().ToString("N");
        (Guid employeeId, string appUserId) = await HrTestSeeding.HireEmployeeAsync(
            sp, $"{label}-{suffix}@example.com", jobTitle: "Reviewer", lastName: label);

        return (appUserId, employeeId);
    }
}
