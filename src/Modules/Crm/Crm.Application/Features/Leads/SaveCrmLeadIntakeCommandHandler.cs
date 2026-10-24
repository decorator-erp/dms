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

/// <summary>Creates the intake lazily on first save, otherwise edits in place, and keeps the
/// <see cref="CrmLeadIntakeResidential"/> child in step with the sector: written from
/// <see cref="CrmLeadIntakeData.Residential"/> while the sector is Residential (left as it is when the block is
/// omitted), deleted when the sector moves away, and refused outright when sent with any other sector. The
/// delete lives here rather than in <c>CrmLeadIntake.Update</c> because Domain has no persistence authority
/// over a row it doesn't directly own the collection of.</summary>
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

        ResidentialIntakeData? residentialData = ToDomain(command.Data.Residential);
        if (residentialData is not null && data.Sector != ProjectSector.Residential)
        {
            return CrmLeadErrors.ResidentialWithoutSector;
        }

        // No .Include(i => i.Residential) - CrmLeadIntakeConfiguration.Ignore(i => i.Residential)
        // means EF has no navigation to traverse there (it's configured from the child's own side,
        // parent-configures-nothing, per that configuration's own remarks); Include on an ignored
        // property throws "does not represent a property access", not a silent no-op.
        CrmLeadIntake? intake = await db.CrmLeadIntakes
            .FirstOrDefaultAsync(i => i.LeadId == command.LeadId, ct);

        CrmLeadIntakeResidential? residential = null;
        if (intake is null)
        {
            Result<CrmLeadIntake> created = CrmLeadIntake.Create(command.LeadId, data);
            if (created.IsFailure)
            {
                return created.Error;
            }

            intake = created.Value!;
            db.CrmLeadIntakes.Add(intake);
        }
        else
        {
            Result updated = intake.Update(data);
            if (updated.IsFailure)
            {
                return updated;
            }

            // Queried directly by LeadIntakeId, same reason Include can't be used above.
            residential = await db.CrmLeadIntakeResidentials
                .FirstOrDefaultAsync(r => r.LeadIntakeId == intake.Id, ct);
        }

        if (data.Sector != ProjectSector.Residential)
        {
            // Sector switch: the invariant is "Residential exists only while Sector == Residential".
            if (residential is not null)
            {
                db.CrmLeadIntakeResidentials.Remove(residential);
                intake.DetachResidential();
            }
        }
        else if (residentialData is not null)
        {
            if (residential is null)
            {
                residential = CrmLeadIntakeResidential.Create(intake.Id, residentialData);
                db.CrmLeadIntakeResidentials.Add(residential);
            }
            else
            {
                residential.Update(residentialData);
            }

            intake.AttachResidential(residential);
        }

        await db.SaveChangesAsync(ct);

        return Result.Success();
    }

    private static ResidentialIntakeData? ToDomain(CrmLeadIntakeResidentialData? r) => r is null
        ? null
        : new ResidentialIntakeData(
            r.UnitType is { } unitType ? CrmEnumMapping.ToDomain(unitType) : null,
            r.UnitCondition is { } unitCondition ? CrmEnumMapping.ToDomain(unitCondition) : null,
            r.IsLicensed, r.IsRented,
            r.FinishingPurpose is { } purpose ? CrmEnumMapping.ToDomain(purpose) : null);
}
