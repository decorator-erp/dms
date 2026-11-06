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
}
