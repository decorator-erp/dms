using BuildingBlocks.Messaging;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts.Templates;
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
    }
}

internal sealed class CreateStageCommandHandler(ProjectsDbContext db) : ICommandHandler<CreateStageCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreateStageCommand command, CancellationToken ct = default)
    {
        var templateExists = await db.StageTemplates.AsNoTracking().AnyAsync(t => t.Id == command.StageTemplateId, ct);
        if (!templateExists)
            return Result.Failure<Guid>(Domain.StageTemplateErrors.NotFound);

        var stage = Domain.Stage.Create(command.StageTemplateId, command.Name, command.Order, command.WeightPercent, command.Description);
        db.Stages.Add(stage);
        await db.SaveChangesAsync(ct);
        return stage.Id;
    }
}

internal sealed class UpdateStageCommandHandler(ProjectsDbContext db) : ICommandHandler<UpdateStageCommand>
{
    public async Task<Result> HandleAsync(UpdateStageCommand command, CancellationToken ct = default)
    {
        var stage = await db.Stages.FindAsync([command.StageId], ct);
        if (stage is null)
            return Domain.TemplateEntityErrors.StageNotFound;

        stage.Update(command.Name, command.Order, command.WeightPercent, command.Description);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

internal sealed class DeleteStageCommandHandler(ProjectsDbContext db) : ICommandHandler<DeleteStageCommand>
{
    public async Task<Result> HandleAsync(DeleteStageCommand command, CancellationToken ct = default)
    {
        var stage = await db.Stages.FindAsync([command.StageId], ct);
        if (stage is null)
            return Domain.TemplateEntityErrors.StageNotFound;

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
        List<StageResponse> stages = await db.Stages.AsNoTracking()
            .Where(s => s.StageTemplateId == query.StageTemplateId)
            .OrderBy(s => s.Order)
            .Select(s => new StageResponse(s.Id, s.StageTemplateId, s.Name, s.Description, s.Order, s.WeightPercent))
            .ToListAsync(ct);

        return Result.Success<IReadOnlyList<StageResponse>>(stages);
    }
}
