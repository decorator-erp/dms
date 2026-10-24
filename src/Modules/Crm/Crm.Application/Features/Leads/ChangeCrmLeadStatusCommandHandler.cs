using BuildingBlocks.Messaging;
using Crm.Contracts.Leads;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using SharedKernel;

namespace Crm.Application.Features.Leads;

internal sealed class ChangeCrmLeadStatusCommandValidator : AbstractValidator<ChangeCrmLeadStatusCommand>
{
    public ChangeCrmLeadStatusCommandValidator()
    {
        RuleFor(x => x.LeadId).NotEmpty();

        // Conversion happens only through the convert operation, which links the client back.
        RuleFor(x => x.Status).IsInEnum().NotEqual(Contracts.LeadStatus.Converted);
    }
}

internal sealed class ChangeCrmLeadStatusCommandHandler(CrmDbContext db) : ICommandHandler<ChangeCrmLeadStatusCommand>
{
    public async Task<Result> HandleAsync(ChangeCrmLeadStatusCommand command, CancellationToken ct = default)
    {
        CrmLead? lead = await db.CrmLeads.FindAsync([command.LeadId], ct);
        if (lead is null)
        {
            return CrmLeadErrors.NotFound;
        }

        Result result = lead.ChangeStatus(CrmEnumMapping.ToDomain(command.Status));
        if (result.IsFailure)
        {
            return result;
        }

        return await LeadWrites.SaveAsync(db, ct);
    }
}
