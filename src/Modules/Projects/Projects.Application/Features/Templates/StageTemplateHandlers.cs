using BuildingBlocks.Messaging;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts.Templates;
using Projects.Domain;
using Projects.Infrastructure.Persistence;
using SharedKernel;
using Domain = Projects.Domain;

namespace Projects.Application.Features.Templates;

internal sealed class CreateStageTemplateCommandValidator : AbstractValidator<CreateStageTemplateCommand>
{
    public CreateStageTemplateCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Description).MaximumLength(2000);
    }
}

internal sealed class CreateStageTemplateCommandHandler(ProjectsDbContext db) : ICommandHandler<CreateStageTemplateCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreateStageTemplateCommand command, CancellationToken ct = default)
    {
        var template = Domain.StageTemplate.Create(
            command.Name, ProjectsEnumMapping.ToDomain(command.Type), command.IsDefault, command.Description);
        db.StageTemplates.Add(template);
        Result saved = await StageTemplateWrites.SaveAsync(db, ct);
        return saved.IsFailure ? Result.Failure<Guid>(saved.Error) : template.Id;
    }
}

internal sealed class UpdateStageTemplateCommandValidator : AbstractValidator<UpdateStageTemplateCommand>
{
    public UpdateStageTemplateCommandValidator()
    {
        RuleFor(x => x.StageTemplateId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200).When(x => x.Name is not null);
        RuleFor(x => x.Description).MaximumLength(2000);
    }
}

internal sealed class UpdateStageTemplateCommandHandler(ProjectsDbContext db) : ICommandHandler<UpdateStageTemplateCommand>
{
    public async Task<Result> HandleAsync(UpdateStageTemplateCommand command, CancellationToken ct = default)
    {
        StageTemplate? template = await db.StageTemplates.FindAsync([command.StageTemplateId], ct);
        if (template is null)
        {
            return Domain.StageTemplateErrors.NotFound;
        }

        template.Update(command.Name, command.Description, command.IsDefault);
        return await StageTemplateWrites.SaveAsync(db, ct);
    }
}

/// <summary>409 <see cref="Domain.StageTemplateErrors.InUse"/> while any project stage was made from one of the
/// template's stages; otherwise the template goes with its stages, their tasks, sub-tasks and departments in the same
/// save (there is no foreign key from <c>stages</c> to <c>stage_templates</c>, so removing the template row alone left
/// them orphaned).</summary>
internal sealed class DeleteStageTemplateCommandHandler(ProjectsDbContext db) : ICommandHandler<DeleteStageTemplateCommand>
{
    public async Task<Result> HandleAsync(DeleteStageTemplateCommand command, CancellationToken ct = default)
    {
        StageTemplate? template = await db.StageTemplates.FindAsync([command.StageTemplateId], ct);
        if (template is null)
        {
            return Domain.StageTemplateErrors.NotFound;
        }

        List<Stage> stages = await db.Stages.Where(s => s.StageTemplateId == template.Id).ToListAsync(ct);
        List<Guid> stageIds = [.. stages.Select(s => s.Id)];
        if (stageIds.Count > 0
            && await db.ProjectStages.AnyAsync(ps => ps.StageId != null && stageIds.Contains(ps.StageId.Value), ct))
        {
            return Domain.StageTemplateErrors.InUse;
        }

        List<StageTask> tasks = await db.StageTasks.Where(t => stageIds.Contains(t.StageId)).ToListAsync(ct);
        List<Guid> taskIds = [.. tasks.Select(t => t.Id)];
        db.StageSubTasks.RemoveRange(await db.StageSubTasks.Where(st => taskIds.Contains(st.StageTaskId)).ToListAsync(ct));
        db.StageTasks.RemoveRange(tasks);
        db.StageDepartments.RemoveRange(await db.StageDepartments.Where(d => stageIds.Contains(d.StageId)).ToListAsync(ct));
        db.Stages.RemoveRange(stages);
        db.StageTemplates.Remove(template);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

internal sealed class GetStageTemplateQueryHandler(ProjectsDbContext db) : IQueryHandler<GetStageTemplateQuery, StageTemplateResponse>
{
    public async Task<Result<StageTemplateResponse>> HandleAsync(GetStageTemplateQuery query, CancellationToken ct = default)
    {
        StageTemplateResponse? response = await db.StageTemplates.AsNoTracking()
            .Where(t => t.Id == query.StageTemplateId)
            .Select(t => new StageTemplateResponse(t.Id, t.Name, t.Description, ProjectsEnumMapping.ToContracts(t.Type), t.IsDefault))
            .FirstOrDefaultAsync(ct);

        return response is null
            ? Result.Failure<StageTemplateResponse>(Domain.StageTemplateErrors.NotFound)
            : response;
    }
}

internal sealed class GetStageTemplatesQueryHandler(ProjectsDbContext db)
    : IQueryHandler<GetStageTemplatesQuery, IReadOnlyList<StageTemplateResponse>>
{
    public async Task<Result<IReadOnlyList<StageTemplateResponse>>> HandleAsync(
        GetStageTemplatesQuery query, CancellationToken ct = default)
    {
        List<StageTemplateResponse> templates = await db.StageTemplates.AsNoTracking()
            .OrderBy(t => t.Name)
            .Select(t => new StageTemplateResponse(t.Id, t.Name, t.Description, ProjectsEnumMapping.ToContracts(t.Type), t.IsDefault))
            .ToListAsync(ct);

        return Result.Success<IReadOnlyList<StageTemplateResponse>>(templates);
    }
}
