using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using BuildingBlocks.Events;
using BuildingBlocks.Messaging;
using BuildingBlocks.Outbox;
using Hr.Contracts;
using Hr.Contracts.Employees;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharedKernel;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// G3-01: hire an employee (HR, which provisions their Identity login as part of the same command) →
/// assert <see cref="IHrDirectory.ResolveEmployeeIdAsync"/> returns the employee id, and that HR issued
/// no SQL against the <c>identity</c> schema. Runs through the REAL composition root
/// (<see cref="ApiFixture"/>'s <c>WebApplicationFactory&lt;Program&gt;</c>), resolving the public
/// command handlers and <see cref="IHrDirectory"/> directly from its <c>Services</c> — every type
/// touched here (<c>CreateEmployeeCommand</c>, <c>IUserProvisioning</c>, <c>IHrDirectory</c>) is public
/// in its module's <c>*.Contracts</c> project, so no <c>InternalsVisibleTo</c> grant is needed to reach
/// any of it, which is itself part of what this test is proving.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class CrossModuleSmokeTests(ApiFixture fixture)
{
    [Fact]
    public async Task Hire_employee_resolves_through_IHrDirectory_with_no_identity_sql()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;

        string suffix = Guid.NewGuid().ToString("N");

        (Guid employeeId, string appUserId) = await HrTestSeeding.HireEmployeeAsync(
            fixture, $"smoke-{suffix}@example.com");

        // Only the resolve call itself is under observation - hiring legitimately touches identity (it
        // provisions the login through IUserProvisioning) and would make this assertion meaningless if
        // included.
        fixture.ClearCapturedSql();

        IHrDirectory hrDirectory = sp.GetRequiredService<IHrDirectory>();
        Guid? resolvedId = await hrDirectory.ResolveEmployeeIdAsync(appUserId, CancellationToken.None);

        Assert.Equal(employeeId, resolvedId);

        // PC-02 liveness: the resolve must have queried hr.employees. Without this, a dead collector
        // makes "issued no SQL touching identity" true for the wrong reason - which is exactly the state
        // this assertion was in for three phases before N1-08.
        Assert.Contains(fixture.CapturedSql, sql => sql.Contains("hr.", StringComparison.OrdinalIgnoreCase));

        var touchedIdentity = fixture.CapturedSql.Where(sql => sql.Contains("identity.", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.True(touchedIdentity.Count == 0, $"HR's ResolveEmployeeIdAsync issued SQL touching identity: {string.Join(" | ", touchedIdentity)}");
    }

    /// <summary>
    /// G6-01: the approval-gate leg, spanning all FOUR modules through the real host and real HTTP
    /// routes (unlike the test above, which resolves handlers directly - Projects' commands need a real
    /// authenticated <c>HttpContext</c> behind <c>ICurrentUser</c>, which a bare DI scope doesn't have).
    /// Register (Identity) → employee (HR) → template/stage/task + project instantiation (Projects) →
    /// Approval-type gate created, requesting an approval (Approvals, via <c>IApprovalRequester</c>) →
    /// decide (Approvals) → delivered through the REAL outbox processor (driven directly rather than
    /// waiting for its 5-second poll) → gate satisfied, task unlocked (Projects again). One flow, four
    /// modules, no direct in-process call to any cascade logic.
    /// </summary>
    [Fact]
    public async Task Approval_gate_flow_spans_all_four_modules_end_to_end()
    {
        HttpClient client = fixture.CreateClient();
        string suffix = Guid.NewGuid().ToString("N");

        (_, string appUserId) = await HrTestSeeding.HireEmployeeOverHttpAsync(
            fixture, $"g6-{suffix}@example.com");

        string token = fixture.IssueTestToken(
            appUserId, "employees.create", "stages.manage", "project.create", "project.read_all",
            "stage_tasks.read", "stage_tasks.manage_gates", "approvals.decide");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage templateResponse = await client.PostAsJsonAsync("/api/stage-templates", new { Name = $"Tmpl-{suffix}", Type = 2 });
        Guid templateId = (await templateResponse.Content.ReadFromJsonAsync<IdBody>())!.StageTemplateId;

        HttpResponseMessage stageResponse = await client.PostAsJsonAsync(
            $"/api/stage-templates/{templateId}/stages",
            new { Name = "Only Stage", Order = 1, WeightPercent = 100m, Description = (string?)null });
        Guid stageId = (await stageResponse.Content.ReadFromJsonAsync<StageIdBody>())!.StageId;

        HttpResponseMessage taskResponse = await client.PostAsJsonAsync(
            $"/api/stages/{stageId}/tasks",
            new { Name = "Needs Approval", Order = 1, Weight = 1.00m, Description = (string?)null, DepartmentId = (Guid?)null });
        Assert.Equal(HttpStatusCode.OK, taskResponse.StatusCode);

        Guid clientId = await CrmTestSeeding.SeedClientAsync(fixture.ConnectionString, appUserId);
        HttpResponseMessage projectResponse = await client.PostAsJsonAsync("/api/projects", new
        {
            Code = $"P-{suffix}",
            Name = "G6 Smoke",
            Address = "1 Test Rd",
            WorkflowType = 1,
            ClientId = clientId,
            StageTemplateId = templateId,
        });
        Assert.Equal(HttpStatusCode.OK, projectResponse.StatusCode);
        Guid projectId = (await projectResponse.Content.ReadFromJsonAsync<ProjectIdBody>())!.ProjectId;

        List<ProjectTaskBody>? tasks = await (await client.GetAsync($"/api/projects/{projectId}/tasks")).Content.ReadFromJsonAsync<List<ProjectTaskBody>>();
        Guid taskId = Assert.Single(tasks!).Id;

        HttpResponseMessage createGateResponse = await client.PostAsJsonAsync($"/api/projects/{projectId}/tasks/{taskId}/gates", new
        {
            ConditionType = 3, // Approval
            Trigger = 1, // Unlock
            Label = "Manager sign-off",
            ReferencedTaskId = (Guid?)null,
            ReferencedStageId = (Guid?)null,
            ApprovalType = 1, // ManagerApproval
        });
        Assert.Equal(HttpStatusCode.OK, createGateResponse.StatusCode);

        List<GateBody>? gates = await (await client.GetAsync($"/api/projects/{projectId}/tasks/{taskId}/gates")).Content.ReadFromJsonAsync<List<GateBody>>();
        GateBody gate = Assert.Single(gates!);
        Assert.NotNull(gate.ApprovalId);
        Assert.Equal(1, gate.Status); // Pending

        HttpResponseMessage decideResponse = await client.PutAsJsonAsync(
            $"/api/approvals/{gate.ApprovalId}/decide", new { Decision = 1, Comment = (string?)null }); // Approved
        Assert.Equal(HttpStatusCode.NoContent, decideResponse.StatusCode);

        OutboxProcessor outboxProcessor = fixture.Services.GetServices<IHostedService>().OfType<OutboxProcessor>().Single();
        await outboxProcessor.ProcessPendingAsync(CancellationToken.None);

        List<GateBody>? gatesAfter = await (await client.GetAsync($"/api/projects/{projectId}/tasks/{taskId}/gates")).Content.ReadFromJsonAsync<List<GateBody>>();
        Assert.Equal(2, Assert.Single(gatesAfter!).Status); // Satisfied

        List<ProjectTaskBody>? tasksAfter = await (await client.GetAsync($"/api/projects/{projectId}/tasks")).Content.ReadFromJsonAsync<List<ProjectTaskBody>>();
        Assert.Equal(2, Assert.Single(tasksAfter!).Status); // Open (was Locked while the gate was pending)
    }

    /// <summary>
    /// G7-08's characterization suite: project → folder tree → upload → revision → download →
    /// Attachment gate → document approval → DocumentApproved gate, extending
    /// <see cref="CrossModuleSmokeTests"/> to all FIVE modules (Identity, HR, Approvals, Projects,
    /// Documents) through the real host and real HTTP routes, run TWICE inside one test for a basic
    /// flake check — each pass uses its own fresh actor/project so the two runs cannot interfere with
    /// each other.
    ///
    /// Both gates are created BEFORE the event that satisfies them (upload for Attachment, the approval
    /// decision for DocumentApproved) — gate satisfaction is reactive to an event that happens AFTER the
    /// gate exists, so this is not a reordering of the plan's own checklist, it is what proving
    /// satisfaction actually requires. The reviewer is the SAME employee as the uploader (self-review) so
    /// one bearer token drives the whole flow, matching this suite's own "no direct in-process call"
    /// discipline without needing to juggle two authenticated actors.
    /// </summary>
    [Fact]
    public async Task Documents_flow_spans_all_five_modules_end_to_end_and_is_stable_across_two_runs()
    {
        await RunFlowAsync();
        await RunFlowAsync();

        async Task RunFlowAsync()
        {
            HttpClient client = fixture.CreateClient();
            string suffix = Guid.NewGuid().ToString("N");

            (Guid employeeId, string appUserId) = await HrTestSeeding.HireEmployeeOverHttpAsync(
                fixture, $"g7-{suffix}@example.com");

            string token = fixture.IssueTestToken(
                appUserId, "employees.create", "stages.manage", "project.create", "project.read_all",
                "stage_tasks.read", "stage_tasks.manage_gates", "approvals.decide",
                "documents.upload", "documents.read", "documents.update");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            HttpResponseMessage templateResponse = await client.PostAsJsonAsync("/api/stage-templates", new { Name = $"Tmpl-{suffix}", Type = 2 });
            Guid templateId = (await templateResponse.Content.ReadFromJsonAsync<IdBody>())!.StageTemplateId;

            HttpResponseMessage stageResponse = await client.PostAsJsonAsync(
                $"/api/stage-templates/{templateId}/stages",
                new { Name = "Only Stage", Order = 1, WeightPercent = 100m, Description = (string?)null });
            Guid stageId = (await stageResponse.Content.ReadFromJsonAsync<StageIdBody>())!.StageId;

            HttpResponseMessage taskResponse = await client.PostAsJsonAsync(
                $"/api/stages/{stageId}/tasks",
                new { Name = "Needs Doc", Order = 1, Weight = 1.00m, Description = (string?)null, DepartmentId = (Guid?)null });
            Assert.Equal(HttpStatusCode.OK, taskResponse.StatusCode);

            Guid clientId = await CrmTestSeeding.SeedClientAsync(fixture.ConnectionString, appUserId);
            HttpResponseMessage projectResponse = await client.PostAsJsonAsync("/api/projects", new
            {
                Code = $"P-{suffix}",
                Name = "G7 Smoke",
                Address = "1 Test Rd",
                WorkflowType = 1,
                ClientId = clientId,
                StageTemplateId = templateId,
            });
            Assert.Equal(HttpStatusCode.OK, projectResponse.StatusCode);
            Guid projectId = (await projectResponse.Content.ReadFromJsonAsync<ProjectIdBody>())!.ProjectId;

            List<ProjectTaskBody>? tasks = await (await client.GetAsync($"/api/projects/{projectId}/tasks")).Content.ReadFromJsonAsync<List<ProjectTaskBody>>();
            Guid taskId = Assert.Single(tasks!).Id;

            // Folder tree - a task-scoped folder, so the upload below can pass its id and match scope.
            HttpResponseMessage folderResponse = await client.PostAsJsonAsync(
                $"/api/project-tasks/{taskId}/document-folders", new { Name = "Drawings", ParentFolderId = (Guid?)null });
            Assert.Equal(HttpStatusCode.OK, folderResponse.StatusCode);
            Guid folderId = (await folderResponse.Content.ReadFromJsonAsync<FolderIdBody>())!.FolderId;

            // Attachment gate, created BEFORE the upload it must be satisfied by.
            HttpResponseMessage attachmentGateResponse = await client.PostAsJsonAsync($"/api/projects/{projectId}/tasks/{taskId}/gates", new
            {
                ConditionType = 5, // Attachment
                Trigger = 1, // Unlock
                Label = "Needs a drawing",
                ReferencedTaskId = (Guid?)null,
                ReferencedStageId = (Guid?)null,
                ApprovalType = (int?)null,
                ReferencedDocumentId = (Guid?)null,
            });
            Assert.Equal(HttpStatusCode.OK, attachmentGateResponse.StatusCode);
            Guid attachmentGateId = (await attachmentGateResponse.Content.ReadFromJsonAsync<GateIdBody>())!.GateConditionId;

            // Upload - through real multipart form content, not a JSON body.
            Guid documentId = await UploadAsync(client, $"/api/project-tasks/{taskId}/documents", "v1 contents", folderId);

            OutboxProcessor outboxProcessor = fixture.Services.GetServices<IHostedService>().OfType<OutboxProcessor>().Single();
            await outboxProcessor.ProcessPendingAsync(CancellationToken.None);

            List<GateBody>? gatesAfterUpload = await (await client.GetAsync($"/api/projects/{projectId}/tasks/{taskId}/gates"))
                .Content.ReadFromJsonAsync<List<GateBody>>();
            Assert.Equal(2, gatesAfterUpload!.Single(g => g.Id == attachmentGateId).Status); // Satisfied

            // Revision - a new lineage row, its own id.
            Guid revisionId = await UploadRevisionAsync(client, documentId, "v2 contents");

            // Download - the REVISION's bytes, not v1's.
            HttpResponseMessage downloadResponse = await client.GetAsync($"/api/project-documents/{revisionId}/download");
            Assert.Equal(HttpStatusCode.OK, downloadResponse.StatusCode);
            string downloaded = await downloadResponse.Content.ReadAsStringAsync();
            Assert.Equal("v2 contents", downloaded);

            DocumentDetailBody? revisionDetail = await (await client.GetAsync($"/api/project-documents/{revisionId}"))
                .Content.ReadFromJsonAsync<DocumentDetailBody>();
            Guid rootDocumentId = revisionDetail!.RootDocumentId;

            // DocumentApproved gate, created BEFORE the review decision it must be satisfied by.
            HttpResponseMessage documentApprovedGateResponse = await client.PostAsJsonAsync($"/api/projects/{projectId}/tasks/{taskId}/gates", new
            {
                ConditionType = 10, // DocumentApproved
                Trigger = 1, // Unlock
                Label = "Needs client sign-off",
                ReferencedTaskId = (Guid?)null,
                ReferencedStageId = (Guid?)null,
                ApprovalType = (int?)null,
                ReferencedDocumentId = rootDocumentId,
            });
            Assert.Equal(HttpStatusCode.OK, documentApprovedGateResponse.StatusCode);
            Guid documentApprovedGateId = (await documentApprovedGateResponse.Content.ReadFromJsonAsync<GateIdBody>())!.GateConditionId;

            // Document approval - self-review (same employee as uploader/approver), one actor throughout.
            HttpResponseMessage requestReviewResponse = await client.PostAsJsonAsync(
                $"/api/project-documents/{revisionId}/request-review", new { ApproverEmployeeId = employeeId });
            Assert.Equal(HttpStatusCode.OK, requestReviewResponse.StatusCode);
            Guid approvalId = (await requestReviewResponse.Content.ReadFromJsonAsync<ApprovalIdBody>())!.ApprovalId;

            HttpResponseMessage decideResponse = await client.PutAsJsonAsync(
                $"/api/approvals/{approvalId}/decide", new { Decision = 1, Comment = (string?)null }); // Approved
            Assert.Equal(HttpStatusCode.NoContent, decideResponse.StatusCode);

            // Two real outbox hops: ApprovalDecidedIntegrationEvent (Documents locks + re-enqueues) ->
            // DocumentApprovedIntegrationEvent (Projects satisfies the gate) - a row enqueued mid-pass is
            // not in that pass's own pending snapshot, so this needs two calls, not one.
            await outboxProcessor.ProcessPendingAsync(CancellationToken.None);
            await outboxProcessor.ProcessPendingAsync(CancellationToken.None);

            DocumentDetailBody? lockedDocument = await (await client.GetAsync($"/api/project-documents/{revisionId}"))
                .Content.ReadFromJsonAsync<DocumentDetailBody>();
            Assert.True(lockedDocument!.IsLocked);

            List<GateBody>? gatesAfterApproval = await (await client.GetAsync($"/api/projects/{projectId}/tasks/{taskId}/gates"))
                .Content.ReadFromJsonAsync<List<GateBody>>();
            Assert.Equal(2, gatesAfterApproval!.Single(g => g.Id == documentApprovedGateId).Status); // Satisfied
        }
    }

    /// <summary>
    /// G9-15's own characterization suite: project → CRM client → design brief → send → approve
    /// (Approvals) → <c>CrmBriefApproved</c> gate satisfied → office-authored requirement item → send →
    /// approve (Approvals) → <c>CrmItemsResolved</c> gate satisfied → period close →
    /// <c>CrmPeriodClosed</c> gate satisfied, extending <see cref="CrossModuleSmokeTests"/> to all SIX
    /// modules (Identity, HR, Approvals, CRM, Projects, Documents — Documents joins only via the module
    /// list this host now boots; nothing here uploads a document) through the real host and real HTTP
    /// routes, run TWICE inside one test for a basic flake check — same shape as this file's own
    /// Documents flow (G7-08), each pass uses its own fresh actor/project/client so the two runs cannot
    /// interfere with each other.
    ///
    /// <para>All three CRM gate types are proven here, not just one — <c>CrmBriefApproved</c> and
    /// <c>CrmItemsResolved</c> both route through a real Approvals decision (two real outbox hops each:
    /// <c>ApprovalDecidedIntegrationEvent</c> reaches CRM first, which enqueues its own CRM-specific
    /// event, which Projects consumes second); <c>CrmPeriodClosed</c> needs only one hop, since closing
    /// a period publishes directly, with no Approvals round trip in between.</para>
    /// </summary>
    [Fact]
    public async Task Crm_flow_spans_all_six_modules_end_to_end_and_is_stable_across_two_runs()
    {
        await RunFlowAsync();
        await RunFlowAsync();

        async Task RunFlowAsync()
        {
            HttpClient client = fixture.CreateClient();
            string suffix = Guid.NewGuid().ToString("N");

            (_, string appUserId) = await HrTestSeeding.HireEmployeeOverHttpAsync(
                fixture, $"g9-{suffix}@example.com");

            string token = fixture.IssueTestToken(
                appUserId, "employees.create", "stages.read", "stages.manage", "stages.manage_gates", "project.create",
                "project.read_all", "stage_tasks.read", "clients.create", "clients.read",
                "crm.briefs.manage", "crm.briefs.send",
                "crm.items.create", "crm.items.send", "crm.periods.open", "crm.periods.close",
                "approvals.decide", "approvals.read",
                "crm.meetings.schedule", "crm.meetings.reschedule", "crm.meetings.cancel", "crm.meetings.read");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            HttpResponseMessage clientResponse = await client.PostAsJsonAsync(
                "/api/clients", ClientRequests.NewClientBody("portal"));
            Assert.Equal(HttpStatusCode.Created, clientResponse.StatusCode);
            Guid clientId = (await clientResponse.Content.ReadFromJsonAsync<ClientIdBody>())!.ClientId;

            // The client is a DIFFERENT person from the employee driving this flow - creating a client
            // provisions their own portal login now, where the caller used to hand in an AppUserId and
            // routinely handed in their own. That matters below: every CrmBrief/BriefSpace/
            // RequirementItem approval names the CLIENT as its sole approver
            // (SendCrmBriefCommandHandler's ApproverSpec), so the client-review decisions have to be
            // made with the client's own token. The employee's token is legitimately Forbidden for
            // them - it only ever worked because the two identities used to be the same row.
            string clientAppUserId = (await client.GetFromJsonAsync<ClientAppUserBody>($"/api/clients/{clientId}"))!.AppUserId;
            HttpClient portalClient = fixture.CreateClient();
            portalClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", fixture.IssueTestToken(clientAppUserId, "approvals.decide", "approvals.read"));

            HttpResponseMessage templateResponse = await client.PostAsJsonAsync("/api/stage-templates", new { Name = $"Tmpl-{suffix}", Type = 2 });
            Guid templateId = (await templateResponse.Content.ReadFromJsonAsync<IdBody>())!.StageTemplateId;

            HttpResponseMessage stageResponse = await client.PostAsJsonAsync(
                $"/api/stage-templates/{templateId}/stages",
                new { Name = "Only Stage", Order = 1, WeightPercent = 100m, Description = (string?)null });
            Guid templateStageId = (await stageResponse.Content.ReadFromJsonAsync<StageIdBody>())!.StageId;

            HttpResponseMessage taskResponse = await client.PostAsJsonAsync(
                $"/api/stages/{templateStageId}/tasks",
                new { Name = "Needs Gathering", Order = 1, Weight = 1.00m, Description = (string?)null, DepartmentId = (Guid?)null });
            Assert.Equal(HttpStatusCode.OK, taskResponse.StatusCode);

            HttpResponseMessage projectResponse = await client.PostAsJsonAsync("/api/projects", new
            {
                Code = $"P-{suffix}",
                Name = "G9 Smoke",
                Address = "1 Test Rd",
                WorkflowType = 1,
                ClientId = clientId,
                StageTemplateId = templateId,
            });
            Assert.Equal(HttpStatusCode.OK, projectResponse.StatusCode);
            Guid projectId = (await projectResponse.Content.ReadFromJsonAsync<ProjectIdBody>())!.ProjectId;

            HttpResponseMessage stagesResponse = await client.GetAsync($"/api/projects/{projectId}/stages");
            Assert.Equal(HttpStatusCode.OK, stagesResponse.StatusCode);
            List<ProjectStageBody>? stages = await stagesResponse.Content.ReadFromJsonAsync<List<ProjectStageBody>>();
            Guid stageId = Assert.Single(stages!).Id;

            // --- M1-02: the Meetings spine step ("lead -> convert to client -> meeting scheduled ->
            // design brief -> ...") - propose/reschedule/cancel through the real HTTP surface, proving
            // routing, permission gates, and the real command handlers. Office-only (Propose/Reschedule/
            // Cancel all resolve an EMPLOYEE actor): accepting a slot needs a CONTACT counterparty, which
            // this flow's single reused identity structurally can't provide (the same AppUserId is
            // registered as both the employee and the client above, and CrmAccessGuard always resolves
            // the employee branch first) - that accept/schedule/outbox path is covered end-to-end by
            // Crm.Application.Tests.MeetingFlowTests instead, with a second, separate identity.
            HttpResponseMessage proposeMeetingResponse = await client.PostAsJsonAsync("/api/crm/meetings/propose", new
            {
                ProjectId = projectId,
                Title = "Kickoff",
                Type = 1, // InOffice
                Slots = new[] { new { StartsAtUtc = DateTime.UtcNow.AddDays(1), EndsAtUtc = DateTime.UtcNow.AddDays(1).AddHours(1) } },
            });
            Assert.Equal(HttpStatusCode.Created, proposeMeetingResponse.StatusCode);
            Guid meetingId = (await proposeMeetingResponse.Content.ReadFromJsonAsync<MeetingIdBody>())!.MeetingId;

            HttpResponseMessage getMeetingResponse = await client.GetAsync($"/api/crm/meetings/{meetingId}");
            Assert.Equal(HttpStatusCode.OK, getMeetingResponse.StatusCode);
            CrmMeetingStatusBody? meeting = await getMeetingResponse.Content.ReadFromJsonAsync<CrmMeetingStatusBody>();
            Assert.Equal("Kickoff", meeting!.Title);
            Assert.Equal(1, meeting.Status); // Requested

            DateTime newStart = DateTime.UtcNow.AddDays(3);
            HttpResponseMessage rescheduleResponse = await client.PutAsJsonAsync($"/api/crm/meetings/{meetingId}/reschedule", new
            {
                StartsAtUtc = newStart,
                EndsAtUtc = newStart.AddHours(1),
            });
            Assert.Equal(HttpStatusCode.NoContent, rescheduleResponse.StatusCode);

            HttpResponseMessage afterRescheduleResponse = await client.GetAsync($"/api/crm/meetings/{meetingId}");
            CrmMeetingStatusBody? rescheduled = await afterRescheduleResponse.Content.ReadFromJsonAsync<CrmMeetingStatusBody>();
            Assert.Equal(3, rescheduled!.Status); // Rescheduled

            HttpResponseMessage cancelResponse = await client.PutAsJsonAsync(
                $"/api/crm/meetings/{meetingId}/cancel", new { Reason = "smoke test done" });
            Assert.Equal(HttpStatusCode.NoContent, cancelResponse.StatusCode);

            HttpResponseMessage afterCancelResponse = await client.GetAsync($"/api/crm/meetings/{meetingId}");
            CrmMeetingStatusBody? canceled = await afterCancelResponse.Content.ReadFromJsonAsync<CrmMeetingStatusBody>();
            Assert.Equal(5, canceled!.Status); // Canceled

            OutboxProcessor outboxProcessor = fixture.Services.GetServices<IHostedService>().OfType<OutboxProcessor>().Single();

            // --- CrmBriefApproved: create the stage gate BEFORE the brief it must be satisfied by. ---
            Guid briefGateId = await CreateStageGateAsync(client, projectId, stageId, conditionType: 8, label: "Needs brief approval");

            HttpResponseMessage briefResponse = await client.PostAsJsonAsync($"/api/crm/projects/{projectId}/brief", new
            {
                ProjectStageId = stageId,
                Data = new { UnitType = 1, UnitCondition = 1 },
            });
            Assert.Equal(HttpStatusCode.Created, briefResponse.StatusCode);
            Guid briefId = (await briefResponse.Content.ReadFromJsonAsync<BriefIdBody>())!.BriefId;

            HttpResponseMessage sendBriefResponse = await client.PutAsync($"/api/crm/briefs/{briefId}/send", content: null);
            Assert.Equal(HttpStatusCode.NoContent, sendBriefResponse.StatusCode);

            Guid briefApprovalId = await FindApprovalIdAsync(client, subjectType: "DesignBrief", subjectId: briefId);
            await DecideApprovalAsync(portalClient, briefApprovalId);

            // ApprovalDecidedIntegrationEvent -> CRM marks the brief Approved and enqueues
            // CrmBriefApprovedIntegrationEvent -> Projects satisfies the gate: two real hops.
            await outboxProcessor.ProcessPendingAsync(CancellationToken.None);
            await outboxProcessor.ProcessPendingAsync(CancellationToken.None);

            Assert.Equal(2, await GetGateStatusAsync(client, projectId, stageId, briefGateId)); // Satisfied

            // --- CrmItemsResolved: same two-hop shape, off a requirement item instead of a brief. ---
            Guid itemsGateId = await CreateStageGateAsync(client, projectId, stageId, conditionType: 9, label: "Needs items resolved");

            HttpResponseMessage periodResponse = await client.PostAsJsonAsync($"/api/crm/projects/{projectId}/periods", new
            {
                ProjectStageId = stageId,
                ClosesAtUtc = DateTime.UtcNow.AddDays(7),
            });
            Assert.Equal(HttpStatusCode.Created, periodResponse.StatusCode);
            Guid periodId = (await periodResponse.Content.ReadFromJsonAsync<PeriodIdBody>())!.PeriodId;

            HttpResponseMessage itemResponse = await client.PostAsJsonAsync($"/api/crm/periods/{periodId}/items", new
            {
                ProjectId = projectId,
                Category = 1, // SpaceLayout
                Title = "Kitchen layout",
                Priority = 2, // Medium
            });
            Assert.Equal(HttpStatusCode.Created, itemResponse.StatusCode);
            Guid itemId = (await itemResponse.Content.ReadFromJsonAsync<ItemIdBody>())!.ItemId;

            HttpResponseMessage sendItemResponse = await client.PutAsync($"/api/crm/requirement-items/{itemId}/send", content: null);
            Assert.Equal(HttpStatusCode.NoContent, sendItemResponse.StatusCode);

            Guid itemApprovalId = await FindApprovalIdAsync(client, subjectType: "RequirementItem", subjectId: itemId);
            await DecideApprovalAsync(portalClient, itemApprovalId);

            await outboxProcessor.ProcessPendingAsync(CancellationToken.None);
            await outboxProcessor.ProcessPendingAsync(CancellationToken.None);

            Assert.Equal(2, await GetGateStatusAsync(client, projectId, stageId, itemsGateId)); // Satisfied

            // --- CrmPeriodClosed: one hop - closing the period publishes directly, no Approvals round trip. ---
            Guid periodGateId = await CreateStageGateAsync(client, projectId, stageId, conditionType: 7, label: "Needs period closed");

            HttpResponseMessage closePeriodResponse = await client.PutAsync($"/api/crm/periods/{periodId}/close", content: null);
            Assert.Equal(HttpStatusCode.NoContent, closePeriodResponse.StatusCode);

            await outboxProcessor.ProcessPendingAsync(CancellationToken.None);

            Assert.Equal(2, await GetGateStatusAsync(client, projectId, stageId, periodGateId)); // Satisfied
        }
    }

    /// <summary>
    /// G9-15's own phase-level "three-subscriber assertion" (lesson 2, explicit — deferred here from
    /// G9-13's own PROGRESS.md entry): <c>ApprovalDecidedIntegrationEventHandler</c>'s filter runs
    /// BEFORE any query, per <c>docs/adding-a-module.md</c>'s subject-type-filtered subscription
    /// pattern. Proven the way that doc prescribes and <c>GateFlowTests.ApprovalDecidedEvent_ForADesignBriefSubject_DoesNotTouchAnyGate</c>
    /// already proved for Projects' own subscriber (G5-08): reuse a REAL row's id under the WRONG
    /// subject type and assert that row is untouched. Here it is CRM's own subscriber's turn — a real
    /// <c>CrmDesignBrief</c>, reused as the fake event's <c>SubjectId</c> under <c>GateCondition</c>
    /// (not <c>DesignBrief</c>). A random-guid test would pass even with the filter deleted, since
    /// there'd be no real row to accidentally match; reusing the real id is what makes this test
    /// meaningful.
    /// </summary>
    [Fact]
    public async Task ApprovalDecidedEvent_ForAGateConditionSubject_DoesNotTouchAnyCrmDesignBrief()
    {
        HttpClient client = fixture.CreateClient();
        string suffix = Guid.NewGuid().ToString("N");

        (_, string appUserId) = await HrTestSeeding.HireEmployeeOverHttpAsync(
            fixture, $"g9-filter-{suffix}@example.com");

        string token = fixture.IssueTestToken(
            appUserId, "employees.create", "project.create", "clients.create", "crm.briefs.manage", "crm.briefs.read");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage clientResponse = await client.PostAsJsonAsync("/api/clients", ClientRequests.NewClientBody("client"));
        Assert.Equal(HttpStatusCode.Created, clientResponse.StatusCode);
        Guid clientId = (await clientResponse.Content.ReadFromJsonAsync<ClientIdBody>())!.ClientId;

        // No stage template at all - CreateCrmDesignBriefCommand's ProjectStageId is nullable, and this
        // test only needs a real project + a real brief, not a stage-owned one.
        HttpResponseMessage projectResponse = await client.PostAsJsonAsync("/api/projects", new
        {
            Code = $"P-{suffix}",
            Name = "Filter Test",
            Address = "1 Test Rd",
            WorkflowType = 1,
            ClientId = clientId,
        });
        Assert.Equal(HttpStatusCode.OK, projectResponse.StatusCode);
        Guid projectId = (await projectResponse.Content.ReadFromJsonAsync<ProjectIdBody>())!.ProjectId;

        HttpResponseMessage briefResponse = await client.PostAsJsonAsync($"/api/crm/projects/{projectId}/brief", new
        {
            Data = new { UnitType = 1, UnitCondition = 1 },
        });
        Assert.Equal(HttpStatusCode.Created, briefResponse.StatusCode);
        Guid briefId = (await briefResponse.Content.ReadFromJsonAsync<BriefIdBody>())!.BriefId;

        // Reuses the REAL brief's own id as the fake event's SubjectId, under the WRONG SubjectType
        // (GateCondition, not DesignBrief) - if CrmApprovalDecidedIntegrationEventHandler's filter
        // didn't run first, this brief would flip to Approved.
        // IEventBus is scoped (it resolves per-scope handlers and their DbContexts), so it CANNOT come
        // from fixture.Services - that is the root provider, and asking it for a scoped service throws
        // before this test asserts anything. Same scope-first shape as this file's own
        // Register_then_create_employee test.
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IEventBus eventBus = scope.ServiceProvider.GetRequiredService<IEventBus>();
        await eventBus.PublishAsync(
            new Approvals.Contracts.ApprovalDecidedIntegrationEvent(
                Guid.NewGuid(), Guid.NewGuid(), Approvals.Contracts.ApprovalSubjectType.GateCondition, briefId,
                Approvals.Contracts.ApprovalDecision.Approved, appUserId, DateTimeOffset.UtcNow),
            CancellationToken.None);

        OutboxProcessor outboxProcessor = fixture.Services.GetServices<IHostedService>().OfType<OutboxProcessor>().Single();
        await outboxProcessor.ProcessPendingAsync(CancellationToken.None);

        CrmBriefStatusBody? briefAfter = await (await client.GetAsync($"/api/crm/projects/{projectId}/brief"))
            .Content.ReadFromJsonAsync<CrmBriefStatusBody>();
        Assert.Equal(1, briefAfter!.Status); // still Draft
    }

    private static async Task<Guid> CreateStageGateAsync(HttpClient client, Guid projectId, Guid stageId, int conditionType, string label)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync($"/api/projects/{projectId}/stages/{stageId}/gates", new
        {
            ConditionType = conditionType,
            Trigger = 1, // Unlock
            Label = label,
            ReferencedTaskId = (Guid?)null,
            ReferencedStageId = (Guid?)null,
            ApprovalType = (int?)null,
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<GateIdBody>())!.GateConditionId;
    }

    private static async Task<int> GetGateStatusAsync(HttpClient client, Guid projectId, Guid stageId, Guid gateId)
    {
        List<GateBody>? gates = await (await client.GetAsync($"/api/projects/{projectId}/stages/{stageId}/gates"))
            .Content.ReadFromJsonAsync<List<GateBody>>();
        return gates!.Single(g => g.Id == gateId).Status;
    }

    private static async Task<Guid> FindApprovalIdAsync(HttpClient client, string subjectType, Guid subjectId)
    {
        List<ApprovalTrailItemBody>? trail = await (await client.GetAsync($"/api/approvals/by-subject/{subjectType}/{subjectId}"))
            .Content.ReadFromJsonAsync<List<ApprovalTrailItemBody>>();
        return Assert.Single(trail!).Id;
    }

    private static async Task DecideApprovalAsync(HttpClient client, Guid approvalId)
    {
        HttpResponseMessage response = await client.PutAsJsonAsync($"/api/approvals/{approvalId}/decide", new { Decision = 1, Comment = (string?)null }); // Approved
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static async Task<Guid> UploadAsync(HttpClient client, string url, string fileContents, Guid? folderId)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(fileContents));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        content.Add(fileContent, "file", "drawing.txt");
        if (folderId is { } id)
        {
            content.Add(new StringContent(id.ToString()), "folderId");
        }

        HttpResponseMessage response = await client.PostAsync(url, content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DocumentIdBody>())!.DocumentId;
    }

    private static async Task<Guid> UploadRevisionAsync(HttpClient client, Guid documentId, string fileContents)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(fileContents));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        content.Add(fileContent, "file", "drawing.txt");

        HttpResponseMessage response = await client.PostAsync($"/api/project-documents/{documentId}/revisions", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DocumentIdBody>())!.DocumentId;
    }

    private sealed record IdBody(Guid StageTemplateId);
    private sealed record StageIdBody(Guid StageId);
    private sealed record ProjectIdBody(Guid ProjectId);
    private sealed record ProjectTaskBody(Guid Id, int Status);
    private sealed record GateBody(Guid Id, Guid? ApprovalId, int Status);
    private sealed record EmployeeIdBody(Guid EmployeeId);
    private sealed record FolderIdBody(Guid FolderId);
    private sealed record GateIdBody(Guid GateConditionId);
    private sealed record DocumentIdBody(Guid DocumentId);
    private sealed record ApprovalIdBody(Guid ApprovalId);
    private sealed record DocumentDetailBody(Guid Id, Guid RootDocumentId, bool IsLocked);
    private sealed record ClientIdBody(Guid ClientId);
    private sealed record ClientAppUserBody(string AppUserId);
    private sealed record ProjectStageBody(Guid Id);
    private sealed record BriefIdBody(Guid BriefId);
    private sealed record PeriodIdBody(Guid PeriodId);
    private sealed record ItemIdBody(Guid ItemId);
    private sealed record MeetingIdBody(Guid MeetingId);
    private sealed record CrmMeetingStatusBody(string Title, int Status);
    private sealed record ApprovalTrailItemBody(Guid Id);
    private sealed record CrmBriefStatusBody(int Status);
}
