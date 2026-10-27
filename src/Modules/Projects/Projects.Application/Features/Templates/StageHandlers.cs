using BuildingBlocks.Messaging;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts.Templates;
using Projects.Domain;
using Projects.Infrastructure.Persistence;
using SharedKernel;
using Domain = Projects.Domain;

namespace Projects.Application.Features.Templates;

internal sealed class CreateStageCommandValidator : AbstractValidator<CreateStageCommand>
{
    public CreateStageCommandValidator()
    {
        RuleFor(x => x.StageTemplateId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        // Same cap as a scope item's fee, so any default fee can be copied onto an item, and a value past the
        // numeric(18,2) column is a 400 rather than an overflow at save.
        RuleFor(x => x.DefaultFee).InclusiveBetween(0m, 1_000_000_000_000m).When(x => x.DefaultFee.HasValue);
    }
}

internal sealed class UpdateStageCommandValidator : AbstractValidator<UpdateStageCommand>
{
    public UpdateStageCommandValidator()
    {
        RuleFor(x => x.StageId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200).When(x => x.Name is not null);
        // Same cap as a scope item's fee, so any default fee can be copied onto an item, and a value past the
        // numeric(18,2) column is a 400 rather than an overflow at save.
        RuleFor(x => x.DefaultFee).InclusiveBetween(0m, 1_000_000_000_000m).When(x => x.DefaultFee.HasValue);
    }
}

internal sealed class CreateStageCommandHandler(ProjectsDbContext db) : ICommandHandler<CreateStageCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreateStageCommand command, CancellationToken ct = default)
    {
        bool templateExists = await db.StageTemplates.AsNoTracking().AnyAsync(t => t.Id == command.StageTemplateId, ct);
        if (!templateExists)
        {
            return Result.Failure<Guid>(Domain.StageTemplateErrors.NotFound);
        }

        var stage = Domain.Stage.Create(
            command.StageTemplateId, command.Name, command.Order, command.WeightPercent, command.Description, command.DefaultFee);
        db.Stages.Add(stage);
        await db.SaveChangesAsync(ct);
        return stage.Id;
    }
}

internal sealed class UpdateStageCommandHandler(ProjectsDbContext db) : ICommandHandler<UpdateStageCommand>
{
    public async Task<Result> HandleAsync(UpdateStageCommand command, CancellationToken ct = default)
    {
        Stage? stage = await db.Stages.FindAsync([command.StageId], ct);
        if (stage is null)
        {
            return Domain.TemplateEntityErrors.StageNotFound;
        }

        stage.Update(command.Name, command.Order, command.WeightPercent, command.Description, command.DefaultFee);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

internal sealed class DeleteStageCommandHandler(ProjectsDbContext db) : ICommandHandler<DeleteStageCommand>
{
    public async Task<Result> HandleAsync(DeleteStageCommand command, CancellationToken ct = default)
    {
        Stage? stage = await db.Stages.FindAsync([command.StageId], ct);
        if (stage is null)
        {
            return Domain.TemplateEntityErrors.StageNotFound;
        }

        db.Stages.Remove(stage);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

internal sealed class GetStagesByTemplateQueryHandler(ProjectsDbContext db)
    : IQueryHandler<GetStagesByTemplateQuery, IReadOnlyList<StageResponse>>
{
    public async Task<Result<IReadOnlyList<StageResponse>>> HandleAsync(GetStagesByTemplateQuery query, CancellationToken ct = default)
    {
        var stages = await db.Stages.AsNoTracking()
            .Where(s => s.StageTemplateId == query.StageTemplateId)
            .OrderBy(s => s.Order)
            .Select(s => new { s.Id, s.StageTemplateId, s.Name, s.Description, s.Order, s.WeightPercent, s.DefaultFee })
            .ToListAsync(ct);

        // One grouped count over the stage ids already loaded, never one count per stage; none for an empty template.
        var stageIds = stages.Select(s => s.Id).ToList();
        Dictionary<Guid, int> taskCounts = stageIds.Count == 0
            ? []
            : await db.StageTasks.AsNoTracking()
                .Where(t => stageIds.Contains(t.StageId))
                .GroupBy(t => t.StageId)
                .Select(g => new { StageId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.StageId, g => g.Count, ct);

        return Result.Success<IReadOnlyList<StageResponse>>([.. stages.Select(s => new StageResponse(
            s.Id, s.StageTemplateId, s.Name, s.Description, s.Order, s.WeightPercent, s.DefaultFee,
            taskCounts.GetValueOrDefault(s.Id)))]);
    }
}
