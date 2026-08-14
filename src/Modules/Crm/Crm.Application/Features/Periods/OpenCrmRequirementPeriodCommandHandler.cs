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
        var project = await projectDirectory.GetProjectAsync(command.ProjectId, ct);
        if (project is null)
            return Result.Failure<Guid>(CrmPeriodErrors.NotFound);

        var alreadyOpen = await db.CrmRequirementPeriods.AnyAsync(
            p => p.ProjectId == command.ProjectId && p.Status == RequirementPeriodStatus.Open, ct);
        if (alreadyOpen)
            return Result.Failure<Guid>(CrmPeriodErrors.NotOpen);

        var result = CrmRequirementPeriod.Create(
            command.ProjectId, command.ProjectStageId, command.ClosesAtUtc, command.OpenedById);
        if (result.IsFailure)
            return Result.Failure<Guid>(result.Error);

        var period = result.Value!;
        db.CrmRequirementPeriods.Add(period);
        await db.SaveChangesAsync(ct);

        return period.Id;
    }
}
