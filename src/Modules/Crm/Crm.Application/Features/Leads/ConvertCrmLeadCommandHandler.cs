using BuildingBlocks.Messaging;
using BuildingBlocks.Outbox;
using Crm.Contracts;
using Crm.Contracts.Leads;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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
///
/// <para>The lead row is locked (<c>FOR UPDATE</c>) the same way <c>SaveCrmLeadIntakeCommandHandler</c> locks
/// it, and the intake is read only after the lock. An intake save that holds the lock and commits does not
/// write the lead row, so its <c>xmin</c> alone would let this convert succeed with the intake it read
/// before the save; with the lock, the convert waits and then fills the client from the committed intake.
/// Same transaction rule as the intake save: <c>DomainEventDispatchDecorator</c>'s, or one opened here for
/// an undecorated caller.</para>
/// </summary>
internal sealed class ConvertCrmLeadCommandHandler(CrmDbContext db) : ICommandHandler<ConvertCrmLeadCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(ConvertCrmLeadCommand command, CancellationToken ct = default)
    {
        if (db.Database.CurrentTransaction is not null)
        {
            return await ConvertAsync(command, ct);
        }

        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(ct);
        Result<Guid> result = await ConvertAsync(command, ct);
        if (result.IsSuccess)
        {
            await transaction.CommitAsync(ct);
        }
        else
        {
            await transaction.RollbackAsync(ct);
        }

        return result;
    }

    private async Task<Result<Guid>> ConvertAsync(ConvertCrmLeadCommand command, CancellationToken ct)
    {
        // xmin is selected explicitly: SELECT * omits system columns, and CrmLead maps xmin as its token.
        CrmLead? lead = await db.CrmLeads
            .FromSql($"SELECT *, xmin FROM crm.crm_leads WHERE \"Id\" = {command.LeadId} FOR UPDATE")
            .SingleOrDefaultAsync(ct);
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

        // Hop 1 — fill blanks on the client from the intake. Never overwrites office-entered data. Read after
        // the lock, so an intake save that held it is already committed and visible here.
        CrmLeadIntake? intake = await db.CrmLeadIntakes.AsNoTracking().FirstOrDefaultAsync(i => i.LeadId == command.LeadId, ct);
        if (intake is not null)
        {
            client.FillBlanksFromIntake(intake.JobTitle, intake.ClientAddress);
        }

        db.Outbox().Enqueue(new LeadConvertedIntegrationEvent(
            Guid.NewGuid(), lead.Id, client.Id, DateTimeOffset.UtcNow));

        Result saved = await LeadWrites.SaveAsync(db, ct);
        if (saved.IsFailure)
        {
            return Result.Failure<Guid>(saved.Error);
        }

        return client.Id;
    }
}
