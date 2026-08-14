using BuildingBlocks.Messaging;
using Crm.Contracts.Leads;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using SharedKernel;

namespace Crm.Application.Features.Leads;

internal sealed class ChangeCrmLeadStatusCommandValidator : AbstractValidator<ChangeCrmLeadStatusCommand>
{
    public ChangeCrmLeadStatusCommandValidator() => RuleFor(x => x.LeadId).NotEmpty();
}

internal sealed class ChangeCrmLeadStatusCommandHandler(CrmDbContext db) : ICommandHandler<ChangeCrmLeadStatusCommand>
{
    public async Task<Result> HandleAsync(ChangeCrmLeadStatusCommand command, CancellationToken ct = default)
    {
        var lead = await db.CrmLeads.FindAsync([command.LeadId], ct);
        if (lead is null)
            return CrmLeadErrors.NotFound;

        var result = lead.ChangeStatus(CrmEnumMapping.ToDomain(command.Status));
        if (result.IsFailure)
            return result;

        await db.SaveChangesAsync(ct);

        return Result.Success();
    }
}
