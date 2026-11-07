using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Integration.Tests;

/// <summary>Shared HTTP seeding for the crm-complete Projects route tests. The caller's token needs
/// <c>clients.create</c> and <c>project.create</c>.</summary>
internal static class ProjectTestSeeding
{
    public static async Task<Guid> CreateClientAsync(HttpClient owner, string prefix)
    {
        HttpResponseMessage response = await owner.PostAsJsonAsync("/api/clients", ClientRequests.NewClientBody(prefix));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("clientId").GetGuid();
    }

    /// <summary>A FreeStyle (workflow 2) project with no stages, owned by the caller.</summary>
    public static async Task<Guid> CreateFreeStyleProjectAsync(HttpClient owner, string code, Guid? clientId = null, string? name = null)
    {
        Guid client = clientId ?? await CreateClientAsync(owner, code.ToLowerInvariant());
        HttpResponseMessage project = await owner.PostAsJsonAsync("/api/projects", new
        {
            Code = code,
            Name = name ?? $"Project {code}",
            Address = "1 Member Rd",
            WorkflowType = 2,
            ClientId = client,
        });
        Assert.True(project.StatusCode == HttpStatusCode.OK, await project.Content.ReadAsStringAsync());
        return (await project.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("projectId").GetGuid();
    }

    /// <summary>A Stage-workflow (1) project whose scope of work is confirmed from one template stage
    /// ("Design"), so it has exactly one stage. The caller's token needs <c>stages.manage</c>,
    /// <c>scope.manage</c> and <c>scope.confirm</c> on top of the create codes.</summary>
    public static async Task<(Guid ProjectId, Guid StageId, Guid TemplateStageId)> CreateConfirmedStageProjectAsync(
        HttpClient owner, string code)
    {
        (Guid projectId, Guid templateStageId) = await CreateDraftStageProjectAsync(owner, code);
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync(
            $"/api/projects/{projectId}/scope-of-work/items", new { SourceStageIds = new[] { templateStageId } })).StatusCode);
        HttpResponseMessage confirm = await owner.PostAsync($"/api/projects/{projectId}/scope-of-work/confirm", content: null);
        Assert.True(confirm.StatusCode == HttpStatusCode.OK, await confirm.Content.ReadAsStringAsync());
        JsonElement stages = await owner.GetFromJsonAsync<JsonElement>($"/api/projects/{projectId}/stages");
        return (projectId, Assert.Single(stages.EnumerateArray()).GetProperty("id").GetGuid(), templateStageId);
    }

    /// <summary>A Stage-workflow project with a draft (unconfirmed) scope of work, plus a one-stage template
    /// whose stage id is returned for picking.</summary>
    public static async Task<(Guid ProjectId, Guid TemplateStageId)> CreateDraftStageProjectAsync(HttpClient owner, string code)
    {
        HttpResponseMessage template = await owner.PostAsJsonAsync("/api/stage-templates", new { Name = $"Tmpl-{code}", Type = 2 });
        Assert.True(template.IsSuccessStatusCode, await template.Content.ReadAsStringAsync());
        Guid templateId = (await template.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("stageTemplateId").GetGuid();
        HttpResponseMessage stage = await owner.PostAsJsonAsync($"/api/stage-templates/{templateId}/stages",
            new { Name = "Design", Order = 1, WeightPercent = 100m, Description = (string?)null, DefaultFee = 1000m });
        Assert.True(stage.IsSuccessStatusCode, await stage.Content.ReadAsStringAsync());
        Guid templateStageId = (await stage.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("stageId").GetGuid();

        Guid client = await CreateClientAsync(owner, code.ToLowerInvariant());
        HttpResponseMessage project = await owner.PostAsJsonAsync("/api/projects", new
        {
            Code = code,
            Name = $"Project {code}",
            Address = "1 Stage Rd",
            WorkflowType = 1,
            ClientId = client,
        });
        Assert.True(project.StatusCode == HttpStatusCode.OK, await project.Content.ReadAsStringAsync());
        Guid projectId = (await project.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("projectId").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PutAsJsonAsync($"/api/projects/{projectId}/scope-of-work",
            new { DurationDays = 30, PlannedStartDate = "2026-12-01", AdjustmentAmount = 0m })).StatusCode);
        return (projectId, templateStageId);
    }
}
