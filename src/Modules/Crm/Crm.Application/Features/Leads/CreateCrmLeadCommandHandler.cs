using BuildingBlocks.Messaging;
using Crm.Contracts.Leads;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using Hr.Contracts;
using SharedKernel;

namespace Crm.Application.Features.Leads;

internal sealed class CreateCrmLeadCommandValidator : AbstractValidator<CreateCrmLeadCommand>
{
    public CreateCrmLeadCommandValidator()
    {
        RuleFor(x => x.FirstName).LeadName();
        RuleFor(x => x.LastName).LeadName();
        RuleFor(x => x.Email).LeadEmail().When(x => x.Email is not null);
        RuleFor(x => x.Phone).MaximumLength(CrmLeadValidationRules.PhoneMaxLength);
        RuleFor(x => x.Notes).MaximumLength(CrmLeadValidationRules.NotesMaxLength);
        RuleFor(x => x.Source).IsInEnum();
        RuleFor(x => x.OwnerId).NotEmpty();
    }
}

/// <summary><see cref="CreateCrmLeadCommand.OwnerId"/> must name an active employee —
/// <see cref="IHrDirectory.GetAsync"/> sees active employees only, so one lookup answers both "unknown" (which
/// used to surface as the <c>fk_crm_leads_owner</c> violation, a 500) and "has left".</summary>
internal sealed class CreateCrmLeadCommandHandler(CrmDbContext db, IHrDirectory hrDirectory)
    : ICommandHandler<CreateCrmLeadCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreateCrmLeadCommand command, CancellationToken ct = default)
    {
        if (await hrDirectory.GetAsync(command.OwnerId, ct) is null)
        {
            return Result.Failure<Guid>(CrmLeadErrors.OwnerNotFound);
        }

        Result<CrmLead> leadResult = CrmLead.Create(
            command.FirstName, command.LastName, CrmEnumMapping.ToDomain(command.Source), command.OwnerId,
            command.Email, command.Phone, command.Notes);
        if (leadResult.IsFailure)
        {
            return Result.Failure<Guid>(leadResult.Error);
        }

        CrmLead lead = leadResult.Value!;
        db.CrmLeads.Add(lead);
        await db.SaveChangesAsync(ct);

        return lead.Id;
    }
}
