using Crm.Contracts.Portal;
using SharedKernel;
using Xunit;

namespace Crm.Application.Tests;

/// <summary>FINDING-P1-5: a deactivated project is as absent from the portal as another client's. Its pending
/// approval cannot be decided (404, still undecided) and the portal list omits it.</summary>
public sealed partial class PortalApprovalTests
{
    [Fact]
    public async Task DeactivatedProject_PortalDecideIs404_ApprovalStaysUndecided_AndTheListOmitsTheRow()
    {
        (BriefTestProject p, Guid approvalId) = await SentBriefAsync("portal-inactive");
        Assert.Contains((await ListAsync(p.ClientAppUserId, "pending")).Value!.Items, i => i.Id == approvalId);

        await DeactivateProjectAsync(p.ProjectId);

        Result result = await DecideAsync(p.ClientAppUserId, approvalId, PortalApprovalDecision.Approved);

        AssertError(result, 404, "crm.portal.NotFound");
        Assert.Null((await ApprovalAsync(approvalId)).Decision);
        Assert.DoesNotContain((await ListAsync(p.ClientAppUserId, "pending")).Value!.Items, i => i.Id == approvalId);
    }

    private async Task DeactivateProjectAsync(Guid projectId)
    {
        await using var connection = new Npgsql.NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using Npgsql.NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """UPDATE projects.projects SET "IsActive" = false WHERE "Id" = @id""";
        command.Parameters.AddWithValue("id", projectId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }
}
