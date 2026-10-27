using Crm.Application.Features.Briefs;
using BuildingBlocks.Messaging;
using Crm.Contracts.Periods;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using SharedKernel;

namespace Crm.Application.Features.Periods;

internal sealed class OpenCrmRequirementPeriodCommandValidator : AbstractValidator<OpenCrmRequirementPeriodCommand>
{
    public OpenCrmRequirementPeriodCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.OpenedById).NotEmpty();
    }
}

/// <summary>At most one Open period per project — the partial unique index (G9-07) is the real
/// enforcement; this pre-check exists only to return a graceful <see cref="Result"/> failure instead
/// of a raw Postgres unique-violation exception. <see cref="OpenCrmRequirementPeriodCommand.ProjectId"/>
/// is validated against a real project via <see cref="IProjectDirectory"/> — carries no FK
/// (structural item 1).</summary>
internal sealed class OpenCrmRequirementPeriodCommandHandler(CrmDbContext db, IProjectDirectory projectDirectory)
    : ICommandHandler<OpenCrmRequirementPeriodCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(OpenCrmRequirementPeriodCommand command, CancellationToken ct = default)
    {
        ProjectSummary? project = await projectDirectory.GetProjectAsync(command.ProjectId, ct);
        if (project is null)
        {
            return Result.Failure<Guid>(CrmPeriodErrors.NotFound);
        }

        // A-2 (X-5): closing a period satisfies its stage's gate, so the stage must be this project's.
        if (command.ProjectStageId is { } stageId)
        {
            Result stage = await BriefReferenceValidator.ValidateStageAsync(projectDirectory, command.ProjectId, stageId, ct);
            if (stage.IsFailure)
            {
                return Result.Failure<Guid>(stage.Error);
            }
        }

        bool alreadyOpen = await db.CrmRequirementPeriods.AnyAsync(
            p => p.ProjectId == command.ProjectId && p.Status == RequirementPeriodStatus.Open, ct);
        if (alreadyOpen)
        {
            return Result.Failure<Guid>(CrmPeriodErrors.NotOpen);
        }

        Result<CrmRequirementPeriod> result = CrmRequirementPeriod.Create(
            command.ProjectId, command.ProjectStageId, command.ClosesAtUtc, command.OpenedById);
        if (result.IsFailure)
        {
            return Result.Failure<Guid>(result.Error);
        }

        CrmRequirementPeriod period = result.Value!;
        db.CrmRequirementPeriods.Add(period);
        await db.SaveChangesAsync(ct);

        return period.Id;
    }
}
