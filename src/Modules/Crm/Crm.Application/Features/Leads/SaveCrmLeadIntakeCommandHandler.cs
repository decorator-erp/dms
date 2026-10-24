using BuildingBlocks.Messaging;
using Crm.Contracts.Leads;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SharedKernel;
// Contracts.Leads.CrmLeadIntakeData and Domain.CrmLeadIntakeData share a name (both ported from the
// same backend record) - aliasing the Domain side is less noisy than fully qualifying every use below.
using DomainIntakeData = Crm.Domain.CrmLeadIntakeData;

namespace Crm.Application.Features.Leads;

internal sealed class SaveCrmLeadIntakeCommandValidator : AbstractValidator<SaveCrmLeadIntakeCommand>
{
    public SaveCrmLeadIntakeCommandValidator()
    {
        RuleFor(x => x.LeadId).NotEmpty();
        RuleFor(x => x.Data).NotNull().SetValidator(new CrmLeadIntakeDataValidator());
    }
}

/// <summary>The intake's field rules, kept in step with <c>CrmLeadIntakeConfiguration</c>'s column lengths
/// (same pattern as <c>CrmBriefHeadDataValidator</c>): over-long values, undefined enums and a non-positive
/// area are 400s here rather than a 500 or a stored nonsense value.</summary>
internal sealed class CrmLeadIntakeDataValidator : AbstractValidator<Contracts.Leads.CrmLeadIntakeData>
{
    public CrmLeadIntakeDataValidator()
    {
        RuleFor(x => x.JobTitle).MaximumLength(128);
        RuleFor(x => x.ClientAddress).MaximumLength(512);
        RuleFor(x => x.UnitAddress).MaximumLength(512);
        RuleFor(x => x.DesignerExperienceReason).MaximumLength(1000);
        RuleFor(x => x.AreaSqm).GreaterThan(0m).When(x => x.AreaSqm.HasValue);
        RuleFor(x => x.Sector).IsInEnum();
        RuleFor(x => x.DesignerExperience).IsInEnum();
        RuleFor(x => x.Residential!).SetValidator(new CrmLeadIntakeResidentialDataValidator()).When(x => x.Residential is not null);
    }
}

internal sealed class CrmLeadIntakeResidentialDataValidator : AbstractValidator<CrmLeadIntakeResidentialData>
{
    public CrmLeadIntakeResidentialDataValidator()
    {
        RuleFor(x => x.UnitType).IsInEnum();
        RuleFor(x => x.UnitCondition).IsInEnum();
        RuleFor(x => x.FinishingPurpose).IsInEnum();
    }
}

/// <summary>Creates the intake lazily on first save, otherwise edits in place, and keeps the
/// <see cref="CrmLeadIntakeResidential"/> child in step with the sector: written from
/// <see cref="Contracts.Leads.CrmLeadIntakeData.Residential"/> while the sector is Residential (left as it is
/// when the block is omitted), deleted when the sector moves away, and refused outright when sent with any
/// other sector. The delete lives here rather than in <c>CrmLeadIntake.Update</c> because Domain has no
/// persistence authority over a row it doesn't directly own the collection of. A converted lead's intake is
/// frozen.
///
/// <para>The save locks the lead row (<c>FOR UPDATE</c>) before it checks the status, and holds the lock until
/// the command's transaction ends, so it serializes with a convert of the same lead. The save never writes
/// the lead row, so the row's <c>xmin</c> alone would not catch the race. If the convert commits first, the
/// lock waits and then reads Converted (409); if the save locks first, the convert's UPDATE waits until the
/// intake is committed, so the intake lands before the conversion. The transaction is
/// <c>DomainEventDispatchDecorator</c>'s, which wraps every command; one is opened here only for a caller
/// that runs this handler undecorated, since a lock outside a transaction is released at once.</para></summary>
internal sealed class SaveCrmLeadIntakeCommandHandler(CrmDbContext db) : ICommandHandler<SaveCrmLeadIntakeCommand>
{
    public async Task<Result> HandleAsync(SaveCrmLeadIntakeCommand command, CancellationToken ct = default)
    {
        if (db.Database.CurrentTransaction is not null)
        {
            return await SaveAsync(command, ct);
        }

        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(ct);
        Result result = await SaveAsync(command, ct);
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

    private async Task<Result> SaveAsync(SaveCrmLeadIntakeCommand command, CancellationToken ct)
    {
        // xmin is selected explicitly: SELECT * omits system columns, and CrmLead maps xmin as its token.
        CrmLead? lead = await db.CrmLeads
            .FromSql($"SELECT *, xmin FROM crm.crm_leads WHERE \"Id\" = {command.LeadId} FOR UPDATE")
            .SingleOrDefaultAsync(ct);
        if (lead is null)
        {
            return CrmLeadErrors.NotFound;
        }

        // The intake is the pre-client snapshot; once the lead is converted it is frozen, same rule and
        // same 409 as CrmLead.Update.
        if (lead.Status == LeadStatus.Converted)
        {
            return CrmLeadErrors.AlreadyConverted;
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

        return await LeadWrites.SaveAsync(db, ct);
    }

    private static ResidentialIntakeData? ToDomain(CrmLeadIntakeResidentialData? r) => r is null
        ? null
        : new ResidentialIntakeData(
            r.UnitType is { } unitType ? CrmEnumMapping.ToDomain(unitType) : null,
            r.UnitCondition is { } unitCondition ? CrmEnumMapping.ToDomain(unitCondition) : null,
            r.IsLicensed, r.IsRented,
            r.FinishingPurpose is { } purpose ? CrmEnumMapping.ToDomain(purpose) : null);
}
