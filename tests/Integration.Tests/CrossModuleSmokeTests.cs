using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using BuildingBlocks.Messaging;
using BuildingBlocks.Outbox;
using Hr.Contracts;
using Hr.Contracts.Employees;
using Identity.Contracts.Auth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// G3-01: register a user (Identity) → create an employee referencing that user (HR) → assert
/// <see cref="IHrDirectory.ResolveEmployeeIdAsync"/> returns the employee id, and that HR issued no SQL
/// against the <c>identity</c> schema. Runs through the REAL composition root
/// (<see cref="ApiFixture"/>'s <c>WebApplicationFactory&lt;Program&gt;</c>), resolving the public
/// command handlers and <see cref="IHrDirectory"/> directly from its <c>Services</c> — every type
/// touched here (<c>RegisterCommand</c>, <c>CreateEmployeeCommand</c>, <c>IHrDirectory</c>) is public in
/// its module's <c>*.Contracts</c> project, so no <c>InternalsVisibleTo</c> grant is needed to reach any
/// of it, which is itself part of what this test is proving.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class CrossModuleSmokeTests(ApiFixture fixture)
{
    [Fact]
    public async Task Register_then_create_employee_resolves_through_IHrDirectory_with_no_identity_sql()
    {
        using var scope = fixture.Services.CreateScope();
        var sp = scope.ServiceProvider;

        var suffix = Guid.NewGuid().ToString("N");
        var email = $"smoke-{suffix}@example.com";

        var register = sp.GetRequiredService<ICommandHandler<RegisterCommand, string>>();
        var registerResult = await register.HandleAsync(
            new RegisterCommand(new RegisterRequest(email, "P@ssw0rd123!", "Test", "User", null, "employee")));
        Assert.True(registerResult.IsSuccess);
        var appUserId = registerResult.Value!;

        var createEmployee = sp.GetRequiredService<ICommandHandler<CreateEmployeeCommand, Guid>>();
        var createResult = await createEmployee.HandleAsync(
            new CreateEmployeeCommand(appUserId, "Engineer", DateOnly.FromDateTime(DateTime.UtcNow), "FullTime"));
        Assert.True(createResult.IsSuccess);
        var employeeId = createResult.Value;

        // Only the resolve call itself is under observation - registration and employee creation
        // legitimately touch identity (Register writes it directly; CreateEmployee validates AppUserId
        // via IUserDirectory) and would make this assertion meaningless if included.
        fixture.ClearCapturedSql();

        var hrDirectory = sp.GetRequiredService<IHrDirectory>();
        var resolvedId = await hrDirectory.ResolveEmployeeIdAsync(appUserId, CancellationToken.None);

        Assert.Equal(employeeId, resolvedId);

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
        var client = fixture.CreateClient();
        var suffix = Guid.NewGuid().ToString("N");

        var registerResponse = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(
            $"g6-{suffix}@example.com", "P@ssw0rd123!", "Test", "User", null, "employee"));
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);
        var appUserId = (await registerResponse.Content.ReadFromJsonAsync<RegisterResponseBody>())!.UserId;

        var token = fixture.IssueTestToken(
            appUserId, "employees.create", "stages.manage", "project.create", "project.read_all",
            "stage_tasks.read", "stage_tasks.manage_gates", "approvals.decide");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var employeeResponse = await client.PostAsJsonAsync("/api/employees", new
        {
            AppUserId = appUserId,
            JobTitle = "Engineer",
            HireDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ContractType = "FullTime",
        });
        Assert.Equal(HttpStatusCode.OK, employeeResponse.StatusCode);

        var templateResponse = await client.PostAsJsonAsync("/api/stage-templates", new { Name = $"Tmpl-{suffix}", Type = 2 });
        var templateId = (await templateResponse.Content.ReadFromJsonAsync<IdBody>())!.StageTemplateId;

        var stageResponse = await client.PostAsJsonAsync(
            $"/api/stage-templates/{templateId}/stages",
            new { Name = "Only Stage", Order = 1, WeightPercent = 100m, Description = (string?)null });
        var stageId = (await stageResponse.Content.ReadFromJsonAsync<StageIdBody>())!.StageId;

        var taskResponse = await client.PostAsJsonAsync(
            $"/api/stages/{stageId}/tasks",
            new { Name = "Needs Approval", Order = 1, Weight = 1.00m, Description = (string?)null, DepartmentId = (Guid?)null });
        Assert.Equal(HttpStatusCode.OK, taskResponse.StatusCode);

        var projectResponse = await client.PostAsJsonAsync("/api/projects", new
        {
            Code = $"P-{suffix}",
            Name = "G6 Smoke",
            Address = "1 Test Rd",
            WorkflowType = 1,
            ClientId = Guid.NewGuid(),
            StageTemplateId = templateId,
        });
        Assert.Equal(HttpStatusCode.OK, projectResponse.StatusCode);
        var projectId = (await projectResponse.Content.ReadFromJsonAsync<ProjectIdBody>())!.ProjectId;

        var tasks = await (await client.GetAsync($"/api/projects/{projectId}/tasks")).Content.ReadFromJsonAsync<List<ProjectTaskBody>>();
        var taskId = Assert.Single(tasks!).Id;

        var createGateResponse = await client.PostAsJsonAsync($"/api/projects/{projectId}/tasks/{taskId}/gates", new
        {
            ConditionType = 3, // Approval
            Trigger = 1, // Unlock
            Label = "Manager sign-off",
            ReferencedTaskId = (Guid?)null,
            ReferencedStageId = (Guid?)null,
            ApprovalType = 1, // ManagerApproval
        });
        Assert.Equal(HttpStatusCode.OK, createGateResponse.StatusCode);

        var gates = await (await client.GetAsync($"/api/projects/{projectId}/tasks/{taskId}/gates")).Content.ReadFromJsonAsync<List<GateBody>>();
        var gate = Assert.Single(gates!);
        Assert.NotNull(gate.ApprovalId);
        Assert.Equal(1, gate.Status); // Pending

        var decideResponse = await client.PutAsJsonAsync(
            $"/api/approvals/{gate.ApprovalId}/decide", new { Decision = 1, Comment = (string?)null }); // Approved
        Assert.Equal(HttpStatusCode.NoContent, decideResponse.StatusCode);

        var outboxProcessor = fixture.Services.GetServices<IHostedService>().OfType<OutboxProcessor>().Single();
        await outboxProcessor.ProcessPendingAsync(CancellationToken.None);

        var gatesAfter = await (await client.GetAsync($"/api/projects/{projectId}/tasks/{taskId}/gates")).Content.ReadFromJsonAsync<List<GateBody>>();
        Assert.Equal(2, Assert.Single(gatesAfter!).Status); // Satisfied

        var tasksAfter = await (await client.GetAsync($"/api/projects/{projectId}/tasks")).Content.ReadFromJsonAsync<List<ProjectTaskBody>>();
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
            var client = fixture.CreateClient();
            var suffix = Guid.NewGuid().ToString("N");

            var registerResponse = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(
                $"g7-{suffix}@example.com", "P@ssw0rd123!", "Test", "User", null, "employee"));
            Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);
            var appUserId = (await registerResponse.Content.ReadFromJsonAsync<RegisterResponseBody>())!.UserId;

            var token = fixture.IssueTestToken(
                appUserId, "employees.create", "stages.manage", "project.create", "project.read_all",
                "stage_tasks.read", "stage_tasks.manage_gates", "approvals.decide",
                "documents.upload", "documents.read", "documents.update");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var employeeResponse = await client.PostAsJsonAsync("/api/employees", new
            {
                AppUserId = appUserId,
                JobTitle = "Engineer",
                HireDate = DateOnly.FromDateTime(DateTime.UtcNow),
                ContractType = "FullTime",
            });
            Assert.Equal(HttpStatusCode.OK, employeeResponse.StatusCode);
            var employeeId = (await employeeResponse.Content.ReadFromJsonAsync<EmployeeIdBody>())!.EmployeeId;

            var templateResponse = await client.PostAsJsonAsync("/api/stage-templates", new { Name = $"Tmpl-{suffix}", Type = 2 });
            var templateId = (await templateResponse.Content.ReadFromJsonAsync<IdBody>())!.StageTemplateId;

            var stageResponse = await client.PostAsJsonAsync(
                $"/api/stage-templates/{templateId}/stages",
                new { Name = "Only Stage", Order = 1, WeightPercent = 100m, Description = (string?)null });
            var stageId = (await stageResponse.Content.ReadFromJsonAsync<StageIdBody>())!.StageId;

            var taskResponse = await client.PostAsJsonAsync(
                $"/api/stages/{stageId}/tasks",
                new { Name = "Needs Doc", Order = 1, Weight = 1.00m, Description = (string?)null, DepartmentId = (Guid?)null });
            Assert.Equal(HttpStatusCode.OK, taskResponse.StatusCode);

            var projectResponse = await client.PostAsJsonAsync("/api/projects", new
            {
                Code = $"P-{suffix}",
                Name = "G7 Smoke",
                Address = "1 Test Rd",
                WorkflowType = 1,
                ClientId = Guid.NewGuid(),
                StageTemplateId = templateId,
            });
            Assert.Equal(HttpStatusCode.OK, projectResponse.StatusCode);
            var projectId = (await projectResponse.Content.ReadFromJsonAsync<ProjectIdBody>())!.ProjectId;

            var tasks = await (await client.GetAsync($"/api/projects/{projectId}/tasks")).Content.ReadFromJsonAsync<List<ProjectTaskBody>>();
            var taskId = Assert.Single(tasks!).Id;

            // Folder tree - a task-scoped folder, so the upload below can pass its id and match scope.
            var folderResponse = await client.PostAsJsonAsync(
                $"/api/project-tasks/{taskId}/document-folders", new { Name = "Drawings", ParentFolderId = (Guid?)null });
            Assert.Equal(HttpStatusCode.OK, folderResponse.StatusCode);
            var folderId = (await folderResponse.Content.ReadFromJsonAsync<FolderIdBody>())!.FolderId;

            // Attachment gate, created BEFORE the upload it must be satisfied by.
            var attachmentGateResponse = await client.PostAsJsonAsync($"/api/projects/{projectId}/tasks/{taskId}/gates", new
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
            var attachmentGateId = (await attachmentGateResponse.Content.ReadFromJsonAsync<GateIdBody>())!.GateConditionId;

            // Upload - through real multipart form content, not a JSON body.
            var documentId = await UploadAsync(client, $"/api/project-tasks/{taskId}/documents", "v1 contents", folderId);

            var outboxProcessor = fixture.Services.GetServices<IHostedService>().OfType<OutboxProcessor>().Single();
            await outboxProcessor.ProcessPendingAsync(CancellationToken.None);

            var gatesAfterUpload = await (await client.GetAsync($"/api/projects/{projectId}/tasks/{taskId}/gates"))
                .Content.ReadFromJsonAsync<List<GateBody>>();
            Assert.Equal(2, gatesAfterUpload!.Single(g => g.Id == attachmentGateId).Status); // Satisfied

            // Revision - a new lineage row, its own id.
            var revisionId = await UploadRevisionAsync(client, documentId, "v2 contents");

            // Download - the REVISION's bytes, not v1's.
            var downloadResponse = await client.GetAsync($"/api/project-documents/{revisionId}/download");
            Assert.Equal(HttpStatusCode.OK, downloadResponse.StatusCode);
            var downloaded = await downloadResponse.Content.ReadAsStringAsync();
            Assert.Equal("v2 contents", downloaded);

            var revisionDetail = await (await client.GetAsync($"/api/project-documents/{revisionId}"))
                .Content.ReadFromJsonAsync<DocumentDetailBody>();
            var rootDocumentId = revisionDetail!.RootDocumentId;

            // DocumentApproved gate, created BEFORE the review decision it must be satisfied by.
            var documentApprovedGateResponse = await client.PostAsJsonAsync($"/api/projects/{projectId}/tasks/{taskId}/gates", new
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
            var documentApprovedGateId = (await documentApprovedGateResponse.Content.ReadFromJsonAsync<GateIdBody>())!.GateConditionId;

            // Document approval - self-review (same employee as uploader/approver), one actor throughout.
            var requestReviewResponse = await client.PostAsJsonAsync(
                $"/api/project-documents/{revisionId}/request-review", new { ApproverEmployeeId = employeeId });
            Assert.Equal(HttpStatusCode.OK, requestReviewResponse.StatusCode);
            var approvalId = (await requestReviewResponse.Content.ReadFromJsonAsync<ApprovalIdBody>())!.ApprovalId;

            var decideResponse = await client.PutAsJsonAsync(
                $"/api/approvals/{approvalId}/decide", new { Decision = 1, Comment = (string?)null }); // Approved
            Assert.Equal(HttpStatusCode.NoContent, decideResponse.StatusCode);

            // Two real outbox hops: ApprovalDecidedIntegrationEvent (Documents locks + re-enqueues) ->
            // DocumentApprovedIntegrationEvent (Projects satisfies the gate) - a row enqueued mid-pass is
            // not in that pass's own pending snapshot, so this needs two calls, not one.
            await outboxProcessor.ProcessPendingAsync(CancellationToken.None);
            await outboxProcessor.ProcessPendingAsync(CancellationToken.None);

            var lockedDocument = await (await client.GetAsync($"/api/project-documents/{revisionId}"))
                .Content.ReadFromJsonAsync<DocumentDetailBody>();
            Assert.True(lockedDocument!.IsLocked);

            var gatesAfterApproval = await (await client.GetAsync($"/api/projects/{projectId}/tasks/{taskId}/gates"))
                .Content.ReadFromJsonAsync<List<GateBody>>();
            Assert.Equal(2, gatesAfterApproval!.Single(g => g.Id == documentApprovedGateId).Status); // Satisfied
        }
    }

    private static async Task<Guid> UploadAsync(HttpClient client, string url, string fileContents, Guid? folderId)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(fileContents));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        content.Add(fileContent, "file", "drawing.txt");
        if (folderId is { } id)
            content.Add(new StringContent(id.ToString()), "folderId");

        var response = await client.PostAsync(url, content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DocumentIdBody>())!.DocumentId;
    }

    private static async Task<Guid> UploadRevisionAsync(HttpClient client, Guid documentId, string fileContents)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(fileContents));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        content.Add(fileContent, "file", "drawing.txt");

        var response = await client.PostAsync($"/api/project-documents/{documentId}/revisions", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DocumentIdBody>())!.DocumentId;
    }

    private sealed record RegisterResponseBody(string UserId);
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
}
