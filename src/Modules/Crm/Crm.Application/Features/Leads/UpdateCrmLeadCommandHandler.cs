using BuildingBlocks.Messaging;
using Crm.Contracts.Leads;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using SharedKernel;

namespace Crm.Application.Features.Leads;

internal sealed class UpdateCrmLeadCommandValidator : AbstractValidator<UpdateCrmLeadCommand>
{
    public UpdateCrmLeadCommandValidator()
    {
        RuleFor(x => x.LeadId).NotEmpty();
        RuleFor(x => x.FirstName).NotEmpty();
        RuleFor(x => x.LastName).NotEmpty();
        RuleFor(x => x.OwnerId).NotEmpty();
    }
}

internal sealed class UpdateCrmLeadCommandHandler(CrmDbContext db) : ICommandHandler<UpdateCrmLeadCommand>
{
    public async Task<Result> HandleAsync(UpdateCrmLeadCommand command, CancellationToken ct = default)
    {
        var lead = await db.CrmLeads.FindAsync([command.LeadId], ct);
        if (lead is null)
            return CrmLeadErrors.NotFound;

        var result = lead.Update(
            command.FirstName, command.LastName, CrmEnumMapping.ToDomain(command.Source), command.OwnerId,
            command.Email, command.Phone, command.Notes);
        if (result.IsFailure)
            return result;

        await db.SaveChangesAsync(ct);

        return Result.Success();
    }
}
