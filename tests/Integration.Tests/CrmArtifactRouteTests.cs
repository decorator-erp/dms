using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Crm.Contracts;
using Xunit;

namespace Integration.Tests;

/// <summary>FINDING-P1-16: a link that names no subject, or more than one, is still 400
/// <c>crm.artifacts.InvalidSubject</c> — the subject count is checked before the project scope, which would
/// otherwise answer 404 for a missing subject.</summary>
[Collection(ApiCollection.Name)]
public sealed class CrmArtifactRouteTests(ApiFixture fixture)
{
    [Fact]
    public async Task ALinkWithNoSubjectOrTwo_Is400_InvalidSubject()
    {
        HttpClient client = fixture.CreateClient();
        string token = fixture.IssueTestToken(Guid.NewGuid().ToString(), CrmPermissions.Artifacts.Manage);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        object[] bodies =
        [
            new { documentId = Guid.NewGuid(), addedByEmployeeId = Guid.NewGuid() },
            new { documentId = Guid.NewGuid(), briefId = Guid.NewGuid(), meetingId = Guid.NewGuid(), addedByEmployeeId = Guid.NewGuid() },
        ];

        foreach (object body in bodies)
        {
            HttpResponseMessage response = await client.PostAsJsonAsync("/api/crm/artifacts", body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("crm.artifacts.InvalidSubject", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
    }
}
