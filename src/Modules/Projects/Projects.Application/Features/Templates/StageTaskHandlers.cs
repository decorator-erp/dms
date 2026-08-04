using BuildingBlocks.Messaging;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts.Templates;
using Projects.Infrastructure.Persistence;
using SharedKernel;
using Domain = Projects.Domain;

namespace Projects.Application.Features.Templates;

internal sealed class CreateStageTaskCommandValidator : AbstractValidator<CreateStageTaskCommand>
{
    public CreateStageTaskCommandValidator()
    {
        RuleFor(x => x.StageId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
    }
}

internal sealed class CreateStageTaskCommandHandler(ProjectsDbContext db) : ICommandHandler<CreateStageTaskCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreateStageTaskCommand command, CancellationToken ct = default)
    {
        var stageExists = await db.Stages.AsNoTracking().AnyAsync(s => s.Id == command.StageId, ct);
        if (!stageExists)
            return Result.Failure<Guid>(Domain.TemplateEntityErrors.StageNotFound);

        var task = Domain.StageTask.Create(
            command.StageId, command.Name, command.Order, command.Weight, command.Description, command.DepartmentId);
        db.StageTasks.Add(task);
        await db.SaveChangesAsync(ct);
        return task.Id;
    }
}

internal sealed class UpdateStageTaskCommandHandler(ProjectsDbContext db) : ICommandHandler<UpdateStageTaskCommand>
{
    public async Task<Result> HandleAsync(UpdateStageTaskCommand command, CancellationToken ct = default)
    {
        var task = await db.StageTasks.FindAsync([command.StageTaskId], ct);
        if (task is null)
            return Domain.TemplateEntityErrors.StageTaskNotFound;

        task.Update(command.Name, command.Order, command.Weight, command.Description, command.DepartmentId);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

internal sealed class DeleteStageTaskCommandHandler(ProjectsDbContext db) : ICommandHandler<DeleteStageTaskCommand>
{
    public async Task<Result> HandleAsync(DeleteStageTaskCommand command, CancellationToken ct = default)
    {
        var task = await db.StageTasks.FindAsync([command.StageTaskId], ct);
        if (task is null)
            return Domain.TemplateEntityErrors.StageTaskNotFound;

        db.StageTasks.Remove(task);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

internal sealed class GetStageTasksByStageQueryHandler(ProjectsDbContext db)
    : IQueryHandler<GetStageTasksByStageQuery, IReadOnlyList<StageTaskResponse>>
{
    public async Task<Result<IReadOnlyList<StageTaskResponse>>> HandleAsync(GetStageTasksByStageQuery query, CancellationToken ct = default)
    {
        List<StageTaskResponse> tasks = await db.StageTasks.AsNoTracking()
            .Where(t => t.StageId == query.StageId)
            .OrderBy(t => t.Order)
            .Select(t => new StageTaskResponse(t.Id, t.StageId, t.Name, t.Description, t.Order, t.Weight, t.DepartmentId))
            .ToListAsync(ct);

        return Result.Success<IReadOnlyList<StageTaskResponse>>(tasks);
    }
}
