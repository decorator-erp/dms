using BuildingBlocks.Messaging;
using BuildingBlocks.Outbox;
using Crm.Contracts;
using Crm.Contracts.Leads;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Crm.Application.Features.Leads;

internal sealed class ConvertCrmLeadCommandValidator : AbstractValidator<ConvertCrmLeadCommand>
{
    public ConvertCrmLeadCommandValidator()
    {
        RuleFor(x => x.LeadId).NotEmpty();
        RuleFor(x => x.ExistingClientId).NotEmpty();
    }
}

/// <summary>
/// v1 requires an existing client, matching the backend's own scope — a
/// <c>// TODO: v2 — accept CreateClientRequest</c> marks new-client-on-convert as unimplemented there
/// too, so this port doesn't invent it. Validates <see cref="ConvertCrmLeadCommand.ExistingClientId"/>
/// against a real row here — the expensive half of the check <c>CrmLead.Convert</c> (G9-03) left to
/// this handler, since Domain has no database access. Hop 1 of the two-hop fill-blanks flow:
/// <see cref="Client.FillBlanksFromIntake"/> here; the brief-seeding hop (G9-04's
/// <c>CrmDesignBrief.SeedFromIntake</c>) happens when the first brief is created on the project
/// (<c>CreateCrmDesignBriefCommandHandler</c>), not here — this handler only converts
/// the lead and touches the client, it never creates a brief.
/// </summary>
internal sealed class ConvertCrmLeadCommandHandler(CrmDbContext db) : ICommandHandler<ConvertCrmLeadCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(ConvertCrmLeadCommand command, CancellationToken ct = default)
    {
        CrmLead? lead = await db.CrmLeads.FindAsync([command.LeadId], ct);
        if (lead is null)
        {
            return Result.Failure<Guid>(CrmLeadErrors.NotFound);
        }

        Client? client = await db.Clients.FirstOrDefaultAsync(c => c.Id == command.ExistingClientId, ct);
        // A soft-deleted client is as absent as a missing one: never convert into it.
        if (client is null || !client.IsActive)
        {
            return Result.Failure<Guid>(ClientErrors.NotFound);
        }

        Result convertResult = lead.Convert(command.ExistingClientId);
        if (convertResult.IsFailure)
        {
            return Result.Failure<Guid>(convertResult.Error);
        }

        // Hop 1 — fill blanks on the client from the intake. Never overwrites office-entered data.
        CrmLeadIntake? intake = await db.CrmLeadIntakes.AsNoTracking().FirstOrDefaultAsync(i => i.LeadId == command.LeadId, ct);
        if (intake is not null)
        {
            client.FillBlanksFromIntake(intake.JobTitle, intake.ClientAddress);
        }

        db.Outbox().Enqueue(new LeadConvertedIntegrationEvent(
            Guid.NewGuid(), lead.Id, client.Id, DateTimeOffset.UtcNow));

        await db.SaveChangesAsync(ct);

        return client.Id;
    }
}
