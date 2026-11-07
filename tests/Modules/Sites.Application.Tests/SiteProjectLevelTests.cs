using BuildingBlocks.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Projects.Contracts;
using Projects.Contracts.Members;
using SharedKernel;
using Sites.Contracts.Surveys;
using Sites.Domain;
using Xunit;

namespace Sites.Application.Tests;

/// <summary>
/// <b>C6.</b> A site write checks the caller's level on the site's project, through
/// <see cref="IProjectAccessLevelReader"/>: Editor to capture (create, update, submit, records, values,
/// documents), Manager to decide, reopen or delete. Below the level is 403 <c>sites.InsufficientLevel</c>, and
/// that comes before the 409 a closed project gives.
/// </summary>
[Collection(SitesApplicationCollection.Name)]
public sealed class SiteProjectLevelTests(SitesApplicationFixture fixture)
{
    private const string Forbidden = "403 sites.InsufficientLevel";

    [Fact]
    public async Task C6_SiteWrites_NeedEditorToCaptureAndManagerToDecide_BeforeTheClosedProject409()
    {
        await SiteTestData.OwnersAsync(fixture);
        Guid projectId = await SiteTestData.CreateProjectInStateAsync(fixture, status: 2, isActive: true);
        (Guid viewerId, string viewer) = await MemberAsync(projectId, "viewer", ProjectMemberAccess.Viewer);
        (Guid editorId, string editor) = await MemberAsync(projectId, "editor", ProjectMemberAccess.Editor);
        (Guid managerId, string manager) = await MemberAsync(projectId, "manager", ProjectMemberAccess.Manager);

        Assert.Equal(Forbidden, Outcome(await CreateAsync(projectId, viewerId, viewer)));
        Result<Guid> created = await CreateAsync(projectId, editorId, editor);
        Assert.Equal("ok", Outcome(created));
        Guid siteId = created.Value;

        UpdateSiteCommand Update(string user) => new(siteId, new UpdateSiteRequest(new SiteUnitDataRequest(Address: $"by {user}")), user);

        Assert.Equal(Forbidden, Outcome(await RunAsync(viewer, Update(viewer))));
        Assert.Equal("ok", Outcome(await RunAsync(editor, Update(editor))));
        Assert.Equal(Forbidden, Outcome(await RunAsync(viewer, new SubmitSiteCommand(siteId, viewerId, viewer))));
        Assert.Equal("ok", Outcome(await RunAsync(editor, new SubmitSiteCommand(siteId, editorId, editor))));

        Assert.Equal(Forbidden, Outcome(await RunAsync(editor, new DecideSiteCommand(siteId, new DecideSiteRequest(false, "No"), editorId, editor))));
        Assert.Equal("ok", Outcome(await RunAsync(manager, new DecideSiteCommand(siteId, new DecideSiteRequest(false, "No"), managerId, manager))));
        Assert.Equal(Forbidden, Outcome(await RunAsync(editor, new ReopenSiteCommand(siteId, editor))));
        Assert.Equal("ok", Outcome(await RunAsync(manager, new ReopenSiteCommand(siteId, manager))));
        Assert.Equal(Forbidden, Outcome(await RunAsync(editor, new DeleteSiteCommand(siteId, editor))));

        // Level (403) before state (409).
        await ExecuteAsync($"""UPDATE projects.projects SET "Status" = 4 WHERE "Id" = '{projectId}';""");
        Assert.Equal(Forbidden, Outcome(await RunAsync(viewer, Update(viewer))));
        Assert.Equal($"409 {SiteErrors.ProjectClosed.Code}", Outcome(await RunAsync(editor, Update(editor))));
        Assert.Equal($"409 {SiteErrors.ProjectClosed.Code}", Outcome(await RunAsync(manager, new DeleteSiteCommand(siteId, manager))));
    }

    private static string Outcome(Result result) => result.IsSuccess ? "ok" : $"{result.Error.Status} {result.Error.Code}";

    private async Task<(Guid EmployeeId, string AppUserId)> MemberAsync(Guid projectId, string label, ProjectMemberAccess level)
    {
        (Guid employeeId, string userId) hired;
        await using (AsyncServiceScope seed = fixture.CreateScope())
        {
            hired = await HrTestSeeding.HireEmployeeAsync(seed.ServiceProvider, $"c6-{label}-{Guid.NewGuid():N}@example.com");
        }

        // The project's owner adds the member; owning a project is Manager on it.
        await using AsyncServiceScope scope = As(SiteTestData.OwnerUserId);
        Result<Guid> added = await scope.ServiceProvider.GetRequiredService<ICommandHandler<AddProjectMemberCommand, Guid>>()
            .HandleAsync(new AddProjectMemberCommand(projectId, SiteTestData.OwnerUserId, hired.employeeId, level));
        Assert.True(added.IsSuccess, added.IsFailure ? added.Error.ToString() : null);
        return hired;
    }

    private async Task<Result<Guid>> CreateAsync(Guid projectId, Guid employeeId, string userId)
    {
        await using AsyncServiceScope scope = As(userId);
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<CreateSiteCommand, Guid>>()
            .HandleAsync(new CreateSiteCommand(
                new CreateSiteRequest(projectId, new SiteUnitDataRequest(Address: "1 Level St")), employeeId, userId));
    }

    private async Task<Result> RunAsync<TCommand>(string userId, TCommand command) where TCommand : ICommand
    {
        await using AsyncServiceScope scope = As(userId);
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<TCommand>>().HandleAsync(command);
    }

    private AsyncServiceScope As(string userId)
    {
        AsyncServiceScope scope = fixture.CreateScope();
        scope.ServiceProvider.GetRequiredService<TestCurrentUser>().UserId = userId;
        return scope;
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
