using BuildingBlocks.Messaging;
using Crm.Contracts.Briefs;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using SharedKernel;

namespace Crm.Application.Features.Briefs;

internal sealed class UpdateCrmBriefHeadCommandValidator : AbstractValidator<UpdateCrmBriefHeadCommand>
{
    public UpdateCrmBriefHeadCommandValidator() => RuleFor(x => x.BriefId).NotEmpty();
}

internal sealed class UpdateCrmBriefHeadCommandHandler(CrmDbContext db) : ICommandHandler<UpdateCrmBriefHeadCommand>
{
    public async Task<Result> HandleAsync(UpdateCrmBriefHeadCommand command, CancellationToken ct = default)
    {
        var brief = await db.CrmDesignBriefs.FindAsync([command.BriefId], ct);
        if (brief is null)
            return CrmBriefErrors.NotFound;

        var result = brief.UpdateHead(BriefMapping.ToDomain(command.Data), command.LastMeetingId);
        if (result.IsFailure)
            return result;

        await db.SaveChangesAsync(ct);

        return Result.Success();
    }
}
