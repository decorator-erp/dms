using BuildingBlocks.Messaging;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts.Templates;
using Projects.Domain;
using Projects.Infrastructure.Persistence;
using SharedKernel;
using Domain = Projects.Domain;

namespace Projects.Application.Features.Templates;

internal sealed class CreateStageSubTaskCommandValidator : AbstractValidator<CreateStageSubTaskCommand>
{
    public CreateStageSubTaskCommandValidator()
    {
        RuleFor(x => x.StageTaskId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
    }
}

internal sealed class CreateStageSubTaskCommandHandler(ProjectsDbContext db) : ICommandHandler<CreateStageSubTaskCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreateStageSubTaskCommand command, CancellationToken ct = default)
    {
        bool stageTaskExists = await db.StageTasks.AsNoTracking().AnyAsync(t => t.Id == command.StageTaskId, ct);
        if (!stageTaskExists)
        {
            return Result.Failure<Guid>(Domain.TemplateEntityErrors.StageTaskNotFound);
        }

        var subTask = Domain.StageSubTask.Create(command.StageTaskId, command.Title, command.Order, command.IsRequired, command.Description);
        db.StageSubTasks.Add(subTask);
        await db.SaveChangesAsync(ct);
        return subTask.Id;
    }
}

internal sealed class UpdateStageSubTaskCommandHandler(ProjectsDbContext db) : ICommandHandler<UpdateStageSubTaskCommand>
{
    public async Task<Result> HandleAsync(UpdateStageSubTaskCommand command, CancellationToken ct = default)
    {
        StageSubTask? subTask = await db.StageSubTasks.FindAsync([command.StageSubTaskId], ct);
        if (subTask is null)
        {
            return Domain.TemplateEntityErrors.StageSubTaskNotFound;
        }

        subTask.Update(command.Title, command.Order, command.IsRequired, command.Description);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

internal sealed class DeleteStageSubTaskCommandHandler(ProjectsDbContext db) : ICommandHandler<DeleteStageSubTaskCommand>
{
    public async Task<Result> HandleAsync(DeleteStageSubTaskCommand command, CancellationToken ct = default)
    {
        StageSubTask? subTask = await db.StageSubTasks.FindAsync([command.StageSubTaskId], ct);
        if (subTask is null)
        {
            return Domain.TemplateEntityErrors.StageSubTaskNotFound;
        }

        db.StageSubTasks.Remove(subTask);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

internal sealed class GetStageSubTasksByStageTaskQueryHandler(ProjectsDbContext db)
    : IQueryHandler<GetStageSubTasksByStageTaskQuery, IReadOnlyList<StageSubTaskResponse>>
{
    public async Task<Result<IReadOnlyList<StageSubTaskResponse>>> HandleAsync(
        GetStageSubTasksByStageTaskQuery query, CancellationToken ct = default)
    {
        List<StageSubTaskResponse> subTasks = await db.StageSubTasks.AsNoTracking()
            .Where(s => s.StageTaskId == query.StageTaskId)
            .OrderBy(s => s.Order)
            .Select(s => new StageSubTaskResponse(s.Id, s.StageTaskId, s.Title, s.Description, s.Order, s.IsRequired))
            .ToListAsync(ct);

        return Result.Success<IReadOnlyList<StageSubTaskResponse>>(subTasks);
    }
}
