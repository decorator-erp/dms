using BuildingBlocks.Messaging;
using Crm.Contracts.Periods;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using SharedKernel;

namespace Crm.Application.Features.Periods;

internal sealed class ExtendCrmRequirementPeriodCommandValidator : AbstractValidator<ExtendCrmRequirementPeriodCommand>
{
    public ExtendCrmRequirementPeriodCommandValidator() => RuleFor(x => x.PeriodId).NotEmpty();
}

internal sealed class ExtendCrmRequirementPeriodCommandHandler(CrmDbContext db) : ICommandHandler<ExtendCrmRequirementPeriodCommand>
{
    public async Task<Result> HandleAsync(ExtendCrmRequirementPeriodCommand command, CancellationToken ct = default)
    {
        CrmRequirementPeriod? period = await db.CrmRequirementPeriods.FindAsync([command.PeriodId], ct);
        if (period is null)
        {
            return CrmPeriodErrors.NotFound;
        }

        Result result = period.Extend(command.NewClosesAtUtc);
        if (result.IsFailure)
        {
            return result;
        }

        return await ItemWrites.SaveAsync(db, ct, CrmPeriodErrors.ConcurrentUpdate);
    }
}
