using BuildingBlocks;
using BuildingBlocks.Messaging;
using FluentValidation;
using Identity.Contracts;
using Identity.Contracts.Permissions;
using Identity.Domain;
using Identity.Infrastructure.Persistence;
using Identity.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
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

internal sealed class GrantPermissionOverrideCommandHandler(
    IdentityModuleDbContext db,
    ICurrentUser currentUser,
    IDelegationGuard delegationGuard,
    IEffectivePermissionService effectivePermissions,
    UserManager<ApplicationUser> userManager,
    ISecurityStamps securityStamps)
    : ICommandHandler<GrantPermissionOverrideCommand>
{
    /// <summary>The body is shared with the deny handler: see <see cref="PermissionOverrideWrites"/>.</summary>
    public Task<Result> HandleAsync(GrantPermissionOverrideCommand command, CancellationToken ct = default) =>
        PermissionOverrideWrites.WriteAsync(
            db,
            currentUser,
            delegationGuard,
            effectivePermissions,
            userManager,
            securityStamps,
            new OverrideWrite(
                command.TargetUserId, command.Permission, IsGrant: true, command.Reason, command.ExpiresAtUtc),
            ct);
}
