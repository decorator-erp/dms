using BuildingBlocks.Messaging;
using Crm.Contracts.Leads;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using SharedKernel;

namespace Crm.Application.Features.Leads;

internal sealed class CreateCrmLeadCommandValidator : AbstractValidator<CreateCrmLeadCommand>
{
    public CreateCrmLeadCommandValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty();
        RuleFor(x => x.LastName).NotEmpty();
        RuleFor(x => x.OwnerId).NotEmpty();
    }
}

internal sealed class CreateCrmLeadCommandHandler(CrmDbContext db) : ICommandHandler<CreateCrmLeadCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreateCrmLeadCommand command, CancellationToken ct = default)
    {
        var leadResult = CrmLead.Create(
            command.FirstName, command.LastName, CrmEnumMapping.ToDomain(command.Source), command.OwnerId,
            command.Email, command.Phone, command.Notes);
        if (leadResult.IsFailure)
            return Result.Failure<Guid>(leadResult.Error);

        var lead = leadResult.Value!;
        db.CrmLeads.Add(lead);
        await db.SaveChangesAsync(ct);

        return lead.Id;
    }
}
