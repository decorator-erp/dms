using BuildingBlocks.Messaging;
using Crm.Contracts.Briefs;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using SharedKernel;

namespace Crm.Application.Features.Briefs;

internal sealed class CreateCrmDesignBriefCommandValidator : AbstractValidator<CreateCrmDesignBriefCommand>
{
    public CreateCrmDesignBriefCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.CreatedById).NotEmpty();
        RuleFor(x => x.ProjectStageId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Data).NotNull().SetValidator(new CrmBriefHeadDataValidator());
    }
}

/// <summary>One brief head per (project, stage) — validated here since <c>ProjectId</c>/
/// <c>ProjectStageId</c> carry no FK (structural item 1: crm precedes projects in SchemaOrder).
/// <see cref="ProjectId"/> is validated against a real project via
/// <see cref="IProjectDirectory"/> — the application-code check that stands where a constraint would
/// otherwise be. <see cref="CreateCrmDesignBriefCommand.ProjectStageId"/> is checked the same way, and
/// must belong to that project (<see cref="BriefReferenceValidator"/>).</summary>
internal sealed class CreateCrmDesignBriefCommandHandler(CrmDbContext db, IProjectDirectory projectDirectory)
    : ICommandHandler<CreateCrmDesignBriefCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreateCrmDesignBriefCommand command, CancellationToken ct = default)
    {
        ProjectSummary? project = await projectDirectory.GetProjectAsync(command.ProjectId, ct);
        if (project is null)
        {
            return Result.Failure<Guid>(CrmBriefErrors.ProjectNotFound);
        }

        Result stageResult = await BriefReferenceValidator.ValidateStageAsync(
            projectDirectory, command.ProjectId, command.ProjectStageId, ct);
        if (stageResult.IsFailure)
        {
            return Result.Failure<Guid>(stageResult.Error);
        }

        bool alreadyExists = await db.CrmDesignBriefs.AnyAsync(
            b => b.ProjectId == command.ProjectId && b.ProjectStageId == command.ProjectStageId, ct);
        if (alreadyExists)
        {
            return Result.Failure<Guid>(CrmBriefErrors.AlreadyExists);
        }

        var brief = CrmDesignBrief.Create(
            command.ProjectId, command.CreatedById, command.ProjectStageId, BriefMapping.ToDomain(command.Data));

        db.CrmDesignBriefs.Add(brief);
        Result saved = await BriefWrites.SaveAsync(db, ct);
        if (saved.IsFailure)
        {
            return Result.Failure<Guid>(saved.Error);
        }

        return brief.Id;
    }
}
