using BuildingBlocks;
using BuildingBlocks.Messaging;
using FluentValidation;
using Identity.Contracts;
using Identity.Contracts.Permissions;
using Identity.Domain;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Identity.Application.Features.Permissions.GrantPermissionOverride;

/// <summary>
/// Ported from <c>../backend/Application/Features/Permissions/Commands/GrantOverride</c>. The backend
/// took a <c>CallerUserId</c> field on the command, supplied by its controller from the HTTP principal
/// — here that comes from <see cref="ICurrentUser"/> instead, injected into the handler rather than
/// carried on the command, so the command shape doesn't leak an HTTP-session concern. Permission
/// validity is checked against <see cref="IPermissionCatalog"/>, not a hard-coded list.
/// </summary>
internal sealed class GrantPermissionOverrideCommandValidator : AbstractValidator<GrantPermissionOverrideCommand>
{
    public GrantPermissionOverrideCommandValidator(IPermissionCatalog catalog)
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

internal sealed class GrantPermissionOverrideCommandHandler(IdentityModuleDbContext db, ICurrentUser currentUser)
    : ICommandHandler<GrantPermissionOverrideCommand>
{
    public async Task<Result> HandleAsync(GrantPermissionOverrideCommand command, CancellationToken ct = default)
    {
        if (currentUser.UserId is null)
            return AuthErrors.UnAuthorization;

        var alreadyExists = await db.UserPermissionOverrides.AnyAsync(
            o => o.UserId == command.TargetUserId && o.Permission == command.Permission, ct);
        if (alreadyExists)
            return PermissionErrors.PermissionOverrideAlreadyExists(command.Permission);

        db.UserPermissionOverrides.Add(UserPermissionOverride.Grant(
            command.TargetUserId, command.Permission, currentUser.UserId, command.Reason, command.ExpiresAtUtc));
        await db.SaveChangesAsync(ct);

        return Result.Success();
    }
}
