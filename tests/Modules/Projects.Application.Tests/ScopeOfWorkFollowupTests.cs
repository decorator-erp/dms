using BuildingBlocks.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Projects.Contracts;
using Projects.Contracts.Projects;
using Projects.Contracts.Scope;
using Projects.Contracts.Templates;
using SharedKernel;
using Xunit;

namespace Projects.Application.Tests;

/// <summary>PR D of the CRM backend completion: the scope-of-work follow-ups D-1 (FreeStyle), D-2 (inactive
/// project) and D-3 (a deleted source stage).</summary>
[Collection(ProjectsApplicationCollection.Name)]
public sealed class ScopeOfWorkFollowupTests(ProjectsApplicationFixture fixture)
{
    /// <summary>D-1 (SoW F1, owner default): a FreeStyle project has no stage workflow, so every
    /// <c>scope.manage</c> write and the confirm are 409 <c>projects.scope.WorkflowNotSupported</c>.</summary>
    [Fact]
    public async Task D1_EveryScopeWrite_OnAFreeStyleProject_Is409()
    {
        ScopeTestProject p = await ScopeOfWorkSeeding.CreateProjectAsync(fixture, "d1-free", WorkflowType.FreeStyle);
        (Guid templateId, IReadOnlyList<SeededStage> s) = await ScopeOfWorkSeeding.CreateTemplateAsync(fixture, "D1", 10m);

        Result[] results =
        [
            Plain(await SendAsync<AddScopeOfWorkItemsCommand, IReadOnlyList<ScopeOfWorkItemResponse>>(p,
                new AddScopeOfWorkItemsCommand(p.ProjectId, p.OfficeUserId, [s[0].Id]))),
            Plain(await SendAsync<AddScopeOfWorkTemplateCommand, IReadOnlyList<ScopeOfWorkItemResponse>>(p,
                new AddScopeOfWorkTemplateCommand(p.ProjectId, p.OfficeUserId, templateId))),
            await SendAsync(p, new UpdateScopeOfWorkHeaderCommand(p.ProjectId, p.OfficeUserId, 10, null, 0m, null, null)),
            await SendAsync(p, new UpdateScopeOfWorkItemCommand(p.ProjectId, p.OfficeUserId, Guid.NewGuid(), "Name", null, 1m)),
            await SendAsync(p, new RemoveScopeOfWorkItemCommand(p.ProjectId, p.OfficeUserId, Guid.NewGuid())),
            await SendAsync(p, new ReorderScopeOfWorkItemsCommand(p.ProjectId, p.OfficeUserId, [Guid.NewGuid()])),
            Plain(await SendAsync<ConfirmScopeOfWorkCommand, ConfirmScopeOfWorkResponse>(p,
                new ConfirmScopeOfWorkCommand(p.ProjectId, p.OfficeUserId))),
        ];

        Assert.All(results, r =>
        {
            Assert.True(r.IsFailure, "Expected 409 WorkflowNotSupported, got success.");
            Assert.Equal("projects.scope.WorkflowNotSupported", r.Error.Code);
            Assert.Equal(409, r.Error.Status);
        });

        ScopeOfWorkResponse read = (await GetAsync(p)).Value!;
        Assert.False(read.CanConfirm);
        Assert.Equal("projects.scope.WorkflowNotSupported", read.BlockingReason);
    }

    /// <summary>D-1: the read says whether the scope can be confirmed now, and if not, why — the first rule
    /// the confirm itself would fail on.</summary>
    [Fact]
    public async Task D1_TheRead_SaysWhetherTheScopeCanBeConfirmed_AndWhyNot()
    {
        ScopeTestProject p = await ScopeOfWorkSeeding.CreateProjectAsync(fixture, "d1-blocker");
        (_, IReadOnlyList<SeededStage> s) = await ScopeOfWorkSeeding.CreateTemplateAsync(fixture, "D1b", 10m);

        ScopeOfWorkResponse empty = (await GetAsync(p)).Value!;
        Assert.False(empty.CanConfirm);
        Assert.Equal("projects.scope.Empty", empty.BlockingReason);

        AssertOk(Plain(await SendAsync<AddScopeOfWorkItemsCommand, IReadOnlyList<ScopeOfWorkItemResponse>>(p,
            new AddScopeOfWorkItemsCommand(p.ProjectId, p.OfficeUserId, [s[0].Id]))));
        Assert.Equal("projects.scope.DurationRequired", (await GetAsync(p)).Value!.BlockingReason);

        AssertOk(await SendAsync(p, new UpdateScopeOfWorkHeaderCommand(p.ProjectId, p.OfficeUserId, 10, null, -5m, null, null)));
        Assert.Equal("projects.scope.AdjustmentNoteRequired", (await GetAsync(p)).Value!.BlockingReason);

        AssertOk(await SendAsync(p, new UpdateScopeOfWorkHeaderCommand(p.ProjectId, p.OfficeUserId, 10, null, -50m, "Too much", null)));
        Assert.Equal("projects.scope.NegativeTotal", (await GetAsync(p)).Value!.BlockingReason);

        AssertOk(await SendAsync(p, new UpdateScopeOfWorkHeaderCommand(p.ProjectId, p.OfficeUserId, 10, null, 0m, null, null)));
        ScopeOfWorkResponse ready = (await GetAsync(p)).Value!;
        Assert.True(ready.CanConfirm);
        Assert.Null(ready.BlockingReason);
    }

    /// <summary>D-2: an inactive project is 404 on the read; NP3 makes every write 409 <c>projects.Closed</c> (it was
    /// 404 before the project's own state gated its work).</summary>
    [Fact]
    public async Task D2_AnInactiveProject_Is404OnTheRead_AndClosedOnWrites()
    {
        ScopeTestProject p = await ScopeOfWorkSeeding.CreateProjectAsync(fixture, "d2-inactive");
        (_, IReadOnlyList<SeededStage> s) = await ScopeOfWorkSeeding.CreateTemplateAsync(fixture, "D2", 10m);
        AssertOk(await SendAsync(p, new SetProjectActiveCommand(p.ProjectId, p.OfficeUserId, false)));

        Result read = Plain(await GetAsync(p));
        Result add = Plain(await SendAsync<AddScopeOfWorkItemsCommand, IReadOnlyList<ScopeOfWorkItemResponse>>(p,
            new AddScopeOfWorkItemsCommand(p.ProjectId, p.OfficeUserId, [s[0].Id])));
        Result header = await SendAsync(p, new UpdateScopeOfWorkHeaderCommand(p.ProjectId, p.OfficeUserId, 10, null, 0m, null, null));

        Assert.True(read.IsFailure, "Expected 404, got success.");
        Assert.Equal(404, read.Error.Status);
        Assert.All(new[] { add, header }, r =>
        {
            Assert.True(r.IsFailure, "Expected 409, got success.");
            Assert.Equal(("projects.Closed", 409), (r.Error.Code, r.Error.Status));
        });
    }

    /// <summary>D-3: an item whose template stage was deleted says so, and the confirm names it.</summary>
    [Fact]
    public async Task D3_ADeletedSourceStage_IsFlagged_AndTheConfirmNamesTheItem()
    {
        ScopeTestProject p = await ScopeOfWorkSeeding.CreateProjectAsync(fixture, "d3-missing");
        (_, IReadOnlyList<SeededStage> s) = await ScopeOfWorkSeeding.CreateTemplateAsync(fixture, "D3", 1m, 2m);
        IReadOnlyList<ScopeOfWorkItemResponse> items = (await SendAsync<AddScopeOfWorkItemsCommand, IReadOnlyList<ScopeOfWorkItemResponse>>(p,
            new AddScopeOfWorkItemsCommand(p.ProjectId, p.OfficeUserId, [s[0].Id, s[1].Id]))).Value!;
        AssertOk(await SendAsync(p, new UpdateScopeOfWorkHeaderCommand(p.ProjectId, p.OfficeUserId, 10, null, 0m, null, null)));
        await using (AsyncServiceScope scope = fixture.CreateScope())
        {
            AssertOk(await scope.ServiceProvider.GetRequiredService<ICommandHandler<DeleteStageCommand>>()
                .HandleAsync(new DeleteStageCommand(s[1].Id)));
        }

        ScopeOfWorkResponse read = (await GetAsync(p)).Value!;
        Assert.Equal([false, true], read.Items.Select(i => i.SourceMissing));
        Assert.False(read.CanConfirm);
        Assert.Equal("projects.scope.SourceStageNotFound", read.BlockingReason);

        Result confirm = Plain(await SendAsync<ConfirmScopeOfWorkCommand, ConfirmScopeOfWorkResponse>(p,
            new ConfirmScopeOfWorkCommand(p.ProjectId, p.OfficeUserId)));
        Assert.Equal("projects.scope.SourceStageNotFound", confirm.Error.Code);
        Assert.Contains(items[1].Id.ToString(), confirm.Error.Description, StringComparison.Ordinal);
    }

    private async Task<Result> SendAsync<TCommand>(ScopeTestProject p, TCommand command)
        where TCommand : ICommand
    {
        await using AsyncServiceScope scope = ScopeOfWorkSeeding.OfficeScope(fixture, p);
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<TCommand>>().HandleAsync(command);
    }

    private async Task<Result<TResponse>> SendAsync<TCommand, TResponse>(ScopeTestProject p, TCommand command)
        where TCommand : ICommand<TResponse>
    {
        await using AsyncServiceScope scope = ScopeOfWorkSeeding.OfficeScope(fixture, p);
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<TCommand, TResponse>>().HandleAsync(command);
    }

    private async Task<Result<ScopeOfWorkResponse>> GetAsync(ScopeTestProject p)
    {
        await using AsyncServiceScope scope = ScopeOfWorkSeeding.OfficeScope(fixture, p);
        return await scope.ServiceProvider.GetRequiredService<IQueryHandler<GetScopeOfWorkQuery, ScopeOfWorkResponse>>()
            .HandleAsync(new GetScopeOfWorkQuery(p.ProjectId, p.OfficeUserId));
    }

    private static Result Plain<T>(Result<T> result) => result.IsSuccess ? Result.Success() : result.Error;

    private static void AssertOk(Result result) => Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
}
