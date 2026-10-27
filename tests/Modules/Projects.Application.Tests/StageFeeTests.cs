using BuildingBlocks.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Projects.Contracts;
using Projects.Contracts.Templates;
using SharedKernel;
using Xunit;

namespace Projects.Application.Tests;

/// <summary>SOW-2: a template stage carries an optional default fee, and the stage list reports how many
/// template tasks each stage has.</summary>
[Collection(ProjectsApplicationCollection.Name)]
public sealed class StageFeeTests(ProjectsApplicationFixture fixture)
{
    [Fact]
    public async Task Create_and_update_with_a_default_fee_round_trip()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        Guid templateId = await CreateTemplateAsync(sp);

        Result<Guid> created = await sp.GetRequiredService<ICommandHandler<CreateStageCommand, Guid>>()
            .HandleAsync(new CreateStageCommand(templateId, "Concept", 1, 50m, DefaultFee: 1500.50m));
        Assert.True(created.IsSuccess, created.IsFailure ? created.Error.ToString() : null);
        Assert.Equal(1500.50m, (await GetStageAsync(sp, templateId, created.Value)).DefaultFee);

        Result updated = await sp.GetRequiredService<ICommandHandler<UpdateStageCommand>>()
            .HandleAsync(new UpdateStageCommand(created.Value, DefaultFee: 2000m));
        Assert.True(updated.IsSuccess, updated.IsFailure ? updated.Error.ToString() : null);
        Assert.Equal(2000m, (await GetStageAsync(sp, templateId, created.Value)).DefaultFee);

        // Omitted on update keeps the stored fee, like every other optional stage field.
        Assert.True((await sp.GetRequiredService<ICommandHandler<UpdateStageCommand>>()
            .HandleAsync(new UpdateStageCommand(created.Value, Name: "Concept design"))).IsSuccess);
        StageResponse stage = await GetStageAsync(sp, templateId, created.Value);
        Assert.Equal(2000m, stage.DefaultFee);
        Assert.Equal("Concept design", stage.Name);
    }

    [Fact]
    public async Task A_negative_default_fee_is_400_on_create_and_on_update()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        Guid templateId = await CreateTemplateAsync(sp);

        Result<Guid> created = await sp.GetRequiredService<ICommandHandler<CreateStageCommand, Guid>>()
            .HandleAsync(new CreateStageCommand(templateId, "Concept", 1, 50m, DefaultFee: -1m));
        Assert.True(created.IsFailure);
        Assert.Equal(400, created.Error.Status);

        Guid stageId = (await sp.GetRequiredService<ICommandHandler<CreateStageCommand, Guid>>()
            .HandleAsync(new CreateStageCommand(templateId, "Concept", 1, 50m))).Value;
        Result updated = await sp.GetRequiredService<ICommandHandler<UpdateStageCommand>>()
            .HandleAsync(new UpdateStageCommand(stageId, DefaultFee: -0.01m));
        Assert.True(updated.IsFailure);
        Assert.Equal(400, updated.Error.Status);
    }

    [Fact]
    public async Task A_default_fee_above_the_cap_is_400_and_the_cap_itself_is_accepted()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        Guid templateId = await CreateTemplateAsync(sp);
        ICommandHandler<CreateStageCommand, Guid> create = sp.GetRequiredService<ICommandHandler<CreateStageCommand, Guid>>();

        // Above the cap (the same 1e12 a scope item's fee allows) is a 400, not a numeric overflow (500).
        Result<Guid> tooBig = await create.HandleAsync(new CreateStageCommand(templateId, "Big", 1, 50m, DefaultFee: 10_000_000_000_000m));
        Assert.True(tooBig.IsFailure);
        Assert.Equal(400, tooBig.Error.Status);

        Result<Guid> atCap = await create.HandleAsync(new CreateStageCommand(templateId, "Cap", 1, 50m, DefaultFee: 1_000_000_000_000m));
        Assert.True(atCap.IsSuccess, atCap.IsFailure ? atCap.Error.ToString() : null);

        Result updated = await sp.GetRequiredService<ICommandHandler<UpdateStageCommand>>()
            .HandleAsync(new UpdateStageCommand(atCap.Value, DefaultFee: 1_000_000_000_000.01m));
        Assert.True(updated.IsFailure);
        Assert.Equal(400, updated.Error.Status);
    }

    [Fact]
    public async Task TaskCount_counts_each_stages_template_tasks()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        Guid templateId = await CreateTemplateAsync(sp);
        ICommandHandler<CreateStageCommand, Guid> createStage = sp.GetRequiredService<ICommandHandler<CreateStageCommand, Guid>>();
        Guid empty = (await createStage.HandleAsync(new CreateStageCommand(templateId, "Empty", 1, 50m))).Value;
        Guid busy = (await createStage.HandleAsync(new CreateStageCommand(templateId, "Busy", 2, 50m))).Value;
        ICommandHandler<CreateStageTaskCommand, Guid> createTask = sp.GetRequiredService<ICommandHandler<CreateStageTaskCommand, Guid>>();
        for (int i = 1; i <= 3; i++)
        {
            Assert.True((await createTask.HandleAsync(new CreateStageTaskCommand(busy, $"Task {i}", i))).IsSuccess);
        }

        Assert.Equal(0, (await GetStageAsync(sp, templateId, empty)).TaskCount);
        Assert.Equal(3, (await GetStageAsync(sp, templateId, busy)).TaskCount);
        Assert.Null((await GetStageAsync(sp, templateId, empty)).DefaultFee);
    }

    private static async Task<Guid> CreateTemplateAsync(IServiceProvider sp)
    {
        Result<Guid> template = await sp.GetRequiredService<ICommandHandler<CreateStageTemplateCommand, Guid>>()
            .HandleAsync(new CreateStageTemplateCommand($"Fees-{Guid.NewGuid():N}", StageTemplateType.Custom));
        Assert.True(template.IsSuccess);
        return template.Value;
    }

    private static async Task<StageResponse> GetStageAsync(IServiceProvider sp, Guid templateId, Guid stageId)
    {
        Result<IReadOnlyList<StageResponse>> stages = await sp
            .GetRequiredService<IQueryHandler<GetStagesByTemplateQuery, IReadOnlyList<StageResponse>>>()
            .HandleAsync(new GetStagesByTemplateQuery(templateId));
        Assert.True(stages.IsSuccess);
        return Assert.Single(stages.Value!, s => s.Id == stageId);
    }
}
