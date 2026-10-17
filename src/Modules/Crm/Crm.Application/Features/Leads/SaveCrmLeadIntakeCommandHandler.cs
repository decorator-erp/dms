using BuildingBlocks.Messaging;
using Crm.Contracts.Leads;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
// Contracts.Leads.CrmLeadIntakeData and Domain.CrmLeadIntakeData share a name (both ported from the
// same backend record) - aliasing the Domain side is less noisy than fully qualifying every use below.
using DomainIntakeData = Crm.Domain.CrmLeadIntakeData;

namespace Crm.Application.Features.Leads;

internal sealed class SaveCrmLeadIntakeCommandValidator : AbstractValidator<SaveCrmLeadIntakeCommand>
{
    public SaveCrmLeadIntakeCommandValidator() => RuleFor(x => x.LeadId).NotEmpty();
}

/// <summary>Creates the intake lazily on first save, otherwise edits in place. Owns the sector-switch
/// orchestration <c>CrmLeadIntake.Update</c> (G9-03) deliberately left out: detaches the stale
/// <see cref="CrmLeadIntakeResidential"/> child before applying a new sector, since Domain has no
/// persistence authority to delete a row it doesn't directly own the collection of.</summary>
internal sealed class SaveCrmLeadIntakeCommandHandler(CrmDbContext db) : ICommandHandler<SaveCrmLeadIntakeCommand>
{
    public async Task<Result> HandleAsync(SaveCrmLeadIntakeCommand command, CancellationToken ct = default)
    {
        bool leadExists = await db.CrmLeads.AnyAsync(l => l.Id == command.LeadId, ct);
        if (!leadExists)
        {
            return CrmLeadErrors.NotFound;
        }

        var data = new DomainIntakeData(
            command.Data.JobTitle, command.Data.ClientAddress,
            command.Data.Sector is { } sector ? CrmEnumMapping.ToDomain(sector) : null,
            command.Data.UnitAddress, command.Data.AreaSqm, command.Data.UsedDesignerBefore,
            command.Data.DesignerExperience is { } experience ? CrmEnumMapping.ToDomain(experience) : null,
            command.Data.DesignerExperienceReason);

        // No .Include(i => i.Residential) - CrmLeadIntakeConfiguration.Ignore(i => i.Residential)
        // means EF has no navigation to traverse there (it's configured from the child's own side,
        // parent-configures-nothing, per that configuration's own remarks); Include on an ignored
        // property throws "does not represent a property access", not a silent no-op.
        CrmLeadIntake? intake = await db.CrmLeadIntakes
            .FirstOrDefaultAsync(i => i.LeadId == command.LeadId, ct);

        Result result;
        if (intake is null)
        {
            intake = CrmLeadIntake.Create(command.LeadId, data);
            db.CrmLeadIntakes.Add(intake);
            result = Result.Success();
        }
        else
        {
            result = intake.Update(data);
        }

        if (result.IsFailure)
        {
            return result;
        }

        // Sector switch: detach a stale residential child that no longer matches. Re-attaching a fresh
        // one for the new sector is the caller's own follow-up request against the residential-specific
        // command set (out of this vertical slice's scope) - this handler only keeps the invariant
        // "Residential exists only while Sector == Residential" from going stale. Queried directly by
        // LeadIntakeId, same reason Include can't be used above.
        if (data.Sector != ProjectSector.Residential)
        {
            CrmLeadIntakeResidential? residential = await db.CrmLeadIntakeResidentials
                .FirstOrDefaultAsync(r => r.LeadIntakeId == intake.Id, ct);
            if (residential is not null)
            {
                db.CrmLeadIntakeResidentials.Remove(residential);
                intake.DetachResidential();
            }
        }

        await db.SaveChangesAsync(ct);

        return Result.Success();
    }
}
