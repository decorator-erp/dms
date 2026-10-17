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
    public CreateStageTemplateCommandValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
}

internal sealed class CreateStageTemplateCommandHandler(ProjectsDbContext db) : ICommandHandler<CreateStageTemplateCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreateStageTemplateCommand command, CancellationToken ct = default)
    {
        var template = Domain.StageTemplate.Create(
            command.Name, ProjectsEnumMapping.ToDomain(command.Type), command.IsDefault, command.Description);
        db.StageTemplates.Add(template);
        await db.SaveChangesAsync(ct);
        return template.Id;
    }
}

internal sealed class UpdateStageTemplateCommandValidator : AbstractValidator<UpdateStageTemplateCommand>
{
    public UpdateStageTemplateCommandValidator() => RuleFor(x => x.StageTemplateId).NotEmpty();
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
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

internal sealed class DeleteStageTemplateCommandHandler(ProjectsDbContext db) : ICommandHandler<DeleteStageTemplateCommand>
{
    public async Task<Result> HandleAsync(DeleteStageTemplateCommand command, CancellationToken ct = default)
    {
        StageTemplate? template = await db.StageTemplates.FindAsync([command.StageTemplateId], ct);
        if (template is null)
        {
            return Domain.StageTemplateErrors.NotFound;
        }

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
