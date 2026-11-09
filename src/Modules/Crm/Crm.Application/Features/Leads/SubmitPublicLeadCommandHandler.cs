using BuildingBlocks.Messaging;
using Crm.Contracts.Leads;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using SharedKernel;
using DomainIntakeData = Crm.Domain.CrmLeadIntakeData;

namespace Crm.Application.Features.Leads;

/// <summary>The lead's own field rules (as on create) plus the intake's (as on the office save).</summary>
internal sealed class SubmitPublicLeadCommandValidator : AbstractValidator<SubmitPublicLeadCommand>
{
    public SubmitPublicLeadCommandValidator()
    {
        RuleFor(x => x.FirstName).LeadName();
        RuleFor(x => x.LastName).LeadName();
        RuleFor(x => x.Email).LeadEmail().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Phone).MaximumLength(CrmLeadValidationRules.PhoneMaxLength);
        RuleFor(x => x.Notes).MaximumLength(CrmLeadValidationRules.NotesMaxLength);
        RuleFor(x => x.Intake!).SetValidator(new CrmLeadIntakeDataValidator()).When(x => x.Intake is not null);
    }
}

/// <summary>
/// LI-1: the public intake form's submission. Stores a <see cref="LeadSource.Website"/> lead with no owner
/// (the office assigns one when it picks the lead up) and, when the client answered any project question,
/// the lead's intake with the same domain rules the office save applies: an experience needs "used a
/// designer before", a reason needs an experience, and residential details need the Residential sector.
/// Lead and intake are written by one <c>SaveChanges</c>, so a refused intake stores no lead either.
/// </summary>
internal sealed class SubmitPublicLeadCommandHandler(CrmDbContext db) : ICommandHandler<SubmitPublicLeadCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(SubmitPublicLeadCommand command, CancellationToken ct = default)
    {
        Result<CrmLead> leadResult = CrmLead.Create(
            command.FirstName, command.LastName, LeadSource.Website, ownerId: null,
            command.Email, command.Phone, command.Notes);
        if (leadResult.IsFailure)
        {
            return Result.Failure<Guid>(leadResult.Error);
        }

        CrmLead lead = leadResult.Value!;
        db.CrmLeads.Add(lead);

        if (command.Intake is { } intakeData && HasAnyAnswer(intakeData))
        {
            DomainIntakeData data = LeadIntakeMapping.ToDomain(intakeData);
            ResidentialIntakeData? residentialData = LeadIntakeMapping.ToDomain(intakeData.Residential);
            if (residentialData is not null && data.Sector != ProjectSector.Residential)
            {
                return Result.Failure<Guid>(CrmLeadErrors.ResidentialWithoutSector);
            }

            Result<CrmLeadIntake> intakeResult = CrmLeadIntake.Create(lead.Id, data);
            if (intakeResult.IsFailure)
            {
                return Result.Failure<Guid>(intakeResult.Error);
            }

            CrmLeadIntake intake = intakeResult.Value!;
            db.CrmLeadIntakes.Add(intake);

            if (residentialData is not null)
            {
                var residential = CrmLeadIntakeResidential.Create(intake.Id, residentialData);
                db.CrmLeadIntakeResidentials.Add(residential);
                intake.AttachResidential(residential);
            }
        }

        await db.SaveChangesAsync(ct);
        return lead.Id;
    }

    /// <summary>A form sent with every project question left blank stores no intake row - the same state as
    /// a lead nobody has interviewed yet, which the intake read already answers with empty data.</summary>
    private static bool HasAnyAnswer(Contracts.Leads.CrmLeadIntakeData d) =>
        !string.IsNullOrWhiteSpace(d.JobTitle) || !string.IsNullOrWhiteSpace(d.ClientAddress)
        || d.Sector is not null || !string.IsNullOrWhiteSpace(d.UnitAddress) || d.AreaSqm is not null
        || d.UsedDesignerBefore is not null || d.DesignerExperience is not null
        || !string.IsNullOrWhiteSpace(d.DesignerExperienceReason) || d.Residential is not null;
}
