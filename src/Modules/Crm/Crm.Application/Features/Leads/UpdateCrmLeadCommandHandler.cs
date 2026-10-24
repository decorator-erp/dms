using BuildingBlocks.Messaging;
using Crm.Contracts.Leads;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using Hr.Contracts;
using SharedKernel;

namespace Crm.Application.Features.Leads;

internal sealed class UpdateCrmLeadCommandValidator : AbstractValidator<UpdateCrmLeadCommand>
{
    public UpdateCrmLeadCommandValidator()
    {
        RuleFor(x => x.LeadId).NotEmpty();
        RuleFor(x => x.FirstName).LeadName();
        RuleFor(x => x.LastName).LeadName();
        RuleFor(x => x.Email).LeadEmail().When(x => x.Email is not null);
        RuleFor(x => x.Phone).MaximumLength(CrmLeadValidationRules.PhoneMaxLength);
        RuleFor(x => x.Notes).MaximumLength(CrmLeadValidationRules.NotesMaxLength);
        RuleFor(x => x.Source).IsInEnum();
        RuleFor(x => x.OwnerId).NotEmpty();
    }
}

/// <summary>A new <see cref="UpdateCrmLeadCommand.OwnerId"/> must name an active employee, same check as
/// create. An unchanged owner is not re-checked: a lead whose owner has since left stays editable (its
/// reassignment is then the edit that must name someone active).</summary>
internal sealed class UpdateCrmLeadCommandHandler(CrmDbContext db, IHrDirectory hrDirectory) : ICommandHandler<UpdateCrmLeadCommand>
{
    public async Task<Result> HandleAsync(UpdateCrmLeadCommand command, CancellationToken ct = default)
    {
        CrmLead? lead = await db.CrmLeads.FindAsync([command.LeadId], ct);
        if (lead is null)
        {
            return CrmLeadErrors.NotFound;
        }

        if (command.OwnerId != lead.OwnerId && await hrDirectory.GetAsync(command.OwnerId, ct) is null)
        {
            return CrmLeadErrors.OwnerNotFound;
        }

        Result result = lead.Update(
            command.FirstName, command.LastName, CrmEnumMapping.ToDomain(command.Source), command.OwnerId,
            command.Email, command.Phone, command.Notes);
        if (result.IsFailure)
        {
            return result;
        }

        return await LeadWrites.SaveAsync(db, ct);
    }
}
