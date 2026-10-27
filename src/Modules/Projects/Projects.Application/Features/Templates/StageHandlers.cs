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
        RuleFor(x => x.DefaultFee).GreaterThanOrEqualTo(0m).When(x => x.DefaultFee.HasValue);
    }
}

internal sealed class UpdateStageCommandValidator : AbstractValidator<UpdateStageCommand>
{
    public UpdateStageCommandValidator()
    {
        RuleFor(x => x.StageId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200).When(x => x.Name is not null);
        RuleFor(x => x.DefaultFee).GreaterThanOrEqualTo(0m).When(x => x.DefaultFee.HasValue);
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

        // One grouped count for the whole template, never one count per stage.
        Dictionary<Guid, int> taskCounts = await db.StageTasks.AsNoTracking()
            .Where(t => db.Stages.Any(s => s.Id == t.StageId && s.StageTemplateId == query.StageTemplateId))
            .GroupBy(t => t.StageId)
            .Select(g => new { StageId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.StageId, g => g.Count, ct);

        return Result.Success<IReadOnlyList<StageResponse>>([.. stages.Select(s => new StageResponse(
            s.Id, s.StageTemplateId, s.Name, s.Description, s.Order, s.WeightPercent, s.DefaultFee,
            taskCounts.GetValueOrDefault(s.Id)))]);
    }
}
