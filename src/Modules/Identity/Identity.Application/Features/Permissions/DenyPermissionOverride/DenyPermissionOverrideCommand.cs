using BuildingBlocks;
using BuildingBlocks.Messaging;
using FluentValidation;
using Identity.Contracts;
using Identity.Contracts.Permissions;
using Identity.Domain;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Identity.Application.Features.Permissions.DenyPermissionOverride;

/// <summary>
/// Ported from <c>../backend/Application/Features/Permissions/Commands/DenyOverride</c>, which had a
/// real bug (D7): it built the entity via <c>repo.Adapt(entity)</c> — a Mapster call, not
/// <c>repo.Add(entity)</c> — so a "deny" override was constructed and then never persisted. This
/// version constructs and adds to the DbContext directly (no repository layer exists here to mistake
/// for a mapper).
/// </summary>
internal sealed class DenyPermissionOverrideCommandValidator : AbstractValidator<DenyPermissionOverrideCommand>
{
    public DenyPermissionOverrideCommandValidator(IPermissionCatalog catalog)
    {
        RuleFor(x => x.TargetUserId).NotEmpty();
        RuleFor(x => x.Permission)
            .NotEmpty()
            .Must(p => catalog.All().SelectMany(m => m.Permissions).Contains(p))
            .WithMessage(x => $"'{x.Permission}' is not a recognized permission.");
        RuleFor(x => x.ExpiresAtUtc).GreaterThan(DateTime.UtcNow).When(x => x.ExpiresAtUtc.HasValue);
        RuleFor(x => x.Reason).MaximumLength(500).When(x => x.Reason is not null);
    }
}

internal sealed class DenyPermissionOverrideCommandHandler(IdentityModuleDbContext db, ICurrentUser currentUser)
    : ICommandHandler<DenyPermissionOverrideCommand>
{
    public async Task<Result> HandleAsync(DenyPermissionOverrideCommand command, CancellationToken ct = default)
    {
        if (currentUser.UserId is null)
            return AuthErrors.UnAuthorization;

        var alreadyExists = await db.UserPermissionOverrides.AnyAsync(
            o => o.UserId == command.TargetUserId && o.Permission == command.Permission, ct);
        if (alreadyExists)
            return PermissionErrors.PermissionOverrideAlreadyExists(command.Permission);

        db.UserPermissionOverrides.Add(UserPermissionOverride.Deny(
            command.TargetUserId, command.Permission, currentUser.UserId, command.Reason, command.ExpiresAtUtc));
        await db.SaveChangesAsync(ct);

        return Result.Success();
    }
}
