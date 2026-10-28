using BuildingBlocks.Messaging;
using Crm.Contracts.Briefs;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using SharedKernel;

namespace Crm.Application.Features.Briefs;

internal sealed class CreateCrmDesignBriefCommandValidator : AbstractValidator<CreateCrmDesignBriefCommand>
{
    public CreateCrmDesignBriefCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.CreatedById).NotEmpty();
        RuleFor(x => x.ProjectStageId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Data).NotNull().SetValidator(new CrmBriefHeadDataValidator(unitDetailsMayBeOmitted: true));
    }
}

/// <summary>One brief head per (project, stage) — validated here since <c>ProjectId</c>/
/// <c>ProjectStageId</c> carry no FK (structural item 1: crm precedes projects in SchemaOrder).
/// <see cref="ProjectId"/> is validated against a real project via
/// <see cref="IProjectDirectory"/> — the application-code check that stands where a constraint would
/// otherwise be. <see cref="CreateCrmDesignBriefCommand.ProjectStageId"/> is checked the same way, and
/// must belong to that project (<see cref="BriefReferenceValidator"/>).
///
/// <para>Hop 2 of lead conversion (D1, copy-once): the project's <b>first</b> brief is seeded from the
/// intake of the lead converted into the project's client — the newest conversion if there are several.
/// Fill-blanks only: every value the request carries wins, including <c>UnitType</c>/<c>UnitCondition</c>,
/// which are taken from the intake only when the request omits them, and <c>Sector</c>. Later briefs on the
/// project are never seeded; the intake stays the immutable pre-client snapshot and the brief head is the
/// live truth. A later brief without a sector takes the project's most recent brief's (F3).</para></summary>
internal sealed class CreateCrmDesignBriefCommandHandler(CrmDbContext db, IProjectDirectory projectDirectory)
    : ICommandHandler<CreateCrmDesignBriefCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreateCrmDesignBriefCommand command, CancellationToken ct = default)
    {
        ProjectSummary? project = await projectDirectory.GetProjectAsync(command.ProjectId, ct);
        if (project is null)
        {
            return Result.Failure<Guid>(CrmBriefErrors.ProjectNotFound);
        }

        Result stageResult = await BriefReferenceValidator.ValidateStageAsync(
            projectDirectory, command.ProjectId, command.ProjectStageId, ct);
        if (stageResult.IsFailure)
        {
            return Result.Failure<Guid>(stageResult.Error);
        }

        bool alreadyExists = await db.CrmDesignBriefs.AnyAsync(
            b => b.ProjectId == command.ProjectId && b.ProjectStageId == command.ProjectStageId, ct);
        if (alreadyExists)
        {
            return Result.Failure<Guid>(CrmBriefErrors.AlreadyExists);
        }

        bool firstOnProject = !await db.CrmDesignBriefs.AnyAsync(b => b.ProjectId == command.ProjectId, ct);
        IntakeSeed? seed = firstOnProject ? await FindIntakeSeedAsync(project.ClientId, ct) : null;

        Domain.CrmBriefHeadData head = BriefMapping.ToDomain(command.Data);

        // D-5: the intake's residential-only answers (licensed, rented, finishing purpose) seed only a brief
        // whose sector is Residential or not set — the request's sector first, then the intake's own. Unit
        // type and condition still seed every first brief: the brief requires them whatever its sector.
        ProjectSector? effectiveSector = head.Sector ?? seed?.Intake.Sector;
        bool residentialBrief = effectiveSector is null or ProjectSector.Residential;

        if (command.Data.UnitType == default)
        {
            if (seed?.Residential?.UnitType is not { } unitType)
            {
                return Result.Failure<Guid>(CrmBriefErrors.UnitDetailsRequired);
            }

            head = head with { UnitType = unitType };
        }

        if (command.Data.UnitCondition == default)
        {
            if (seed?.Residential?.UnitCondition is not { } unitCondition)
            {
                return Result.Failure<Guid>(CrmBriefErrors.UnitDetailsRequired);
            }

            head = head with { UnitCondition = unitCondition };
        }

        // Sector: the request's when set; on the first brief, the intake's (SeedFromIntake below only fills
        // a blank); on a later brief, the project's most recent brief's.
        if (head.Sector is null && !firstOnProject)
        {
            ProjectSector? latestSector = await db.CrmDesignBriefs.AsNoTracking()
                .Where(b => b.ProjectId == command.ProjectId)
                .OrderByDescending(b => b.CreatedAtUtc).ThenByDescending(b => b.Id)
                .Select(b => b.Sector)
                .FirstOrDefaultAsync(ct);
            head = head with { Sector = latestSector };
        }

        var brief = CrmDesignBrief.Create(command.ProjectId, command.CreatedById, command.ProjectStageId, head);

        if (seed is { Intake: var intake })
        {
            CrmLeadIntakeResidential? residential = residentialBrief ? seed.Residential : null;
            // Unit type/condition were resolved above (request first), so they are not passed again here:
            // SeedFromIntake overwrites those two rather than filling a blank.
            Result seeded = brief.SeedFromIntake(
                intake.Sector, intake.UnitAddress, intake.AreaSqm, intake.UsedDesignerBefore,
                intake.DesignerExperience, intake.DesignerExperienceReason,
                residentialUnitType: null, residentialUnitCondition: null,
                residential?.IsLicensed, residential?.IsRented, residential?.FinishingPurpose);
            if (seeded.IsFailure)
            {
                return Result.Failure<Guid>(seeded.Error);
            }
        }

        db.CrmDesignBriefs.Add(brief);
        Result saved = await BriefWrites.SaveAsync(db, ct);
        if (saved.IsFailure)
        {
            return Result.Failure<Guid>(saved.Error);
        }

        return brief.Id;
    }

    private sealed record IntakeSeed(CrmLeadIntake Intake, CrmLeadIntakeResidential? Residential);

    private async Task<IntakeSeed?> FindIntakeSeedAsync(Guid clientId, CancellationToken ct)
    {
        CrmLeadIntake? intake = await (
                from lead in db.CrmLeads.AsNoTracking()
                where lead.Status == LeadStatus.Converted && lead.ConvertedClientId == clientId
                join i in db.CrmLeadIntakes.AsNoTracking() on lead.Id equals i.LeadId
                orderby lead.ConvertedAtUtc descending, lead.Id descending
                select i)
            .FirstOrDefaultAsync(ct);
        if (intake is null)
        {
            return null;
        }

        CrmLeadIntakeResidential? residential = await db.CrmLeadIntakeResidentials.AsNoTracking()
            .FirstOrDefaultAsync(r => r.LeadIntakeId == intake.Id, ct);
        return new IntakeSeed(intake, residential);
    }
}
