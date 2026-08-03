using BuildingBlocks.Messaging;
using BuildingBlocks.Outbox;
using FluentValidation;
using Identity.Contracts;
using Identity.Contracts.Auth;
using Identity.Domain;
using Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using SharedKernel;

namespace Identity.Application.Features.Auths.Register;

internal sealed class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    public RegisterCommandValidator()
    {
        RuleFor(x => x.Request.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Request.Password).NotEmpty().MinimumLength(8);
        RuleFor(x => x.Request.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Request.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Request.RoleName).NotEmpty();
    }
}

/// <summary>
/// Ported from the backend's <c>AuthService.RegisterAsync</c>, narrowed: the backend's version created
/// the user AND the employee record in one call, spanning what are now two modules' write models — the
/// thing this architecture exists to prevent (see the HR module's G2-04 notes). This command only ever
/// creates the identity/auth side. Publishes <see cref="UserRegisteredIntegrationEvent"/>, which HR
/// does not subscribe to (it isn't in scope here and Identity is a sink regardless) — it exists for
/// whichever future module needs to react to a new account.
/// </summary>
internal sealed class RegisterCommandHandler(UserManager<ApplicationUser> userManager, RoleManager<ApplicationRole> roleManager, IdentityModuleDbContext db)
    : ICommandHandler<RegisterCommand, string>
{
    public async Task<Result<string>> HandleAsync(RegisterCommand command, CancellationToken ct = default)
    {
        var request = command.Request;

        if (await userManager.FindByEmailAsync(request.Email) is not null)
            return AuthErrors.DuplicatedEmail(request.Email);

        var role = await roleManager.FindByNameAsync(request.RoleName);
        if (role is null)
            return RoleErrors.NotFound;

        var user = ApplicationUser.Create(
            Guid.NewGuid().ToString(), request.Email, request.Email, request.FirstName, request.LastName,
            UserType.Employee, request.PhoneNumber);

        // Staged before CreateAsync, on the SAME context CreateAsync will save through (AutoSaveChanges
        // defaults to true on the EF user store) - one SaveChangesAsync call persists the user and the
        // outbox row together.
        db.Outbox().Enqueue(new UserRegisteredIntegrationEvent(Guid.NewGuid(), user.Id, user.Email!, DateTimeOffset.UtcNow));

        var createResult = await userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
            return AuthErrors.InvalidRegister(string.Join("; ", createResult.Errors.Select(e => e.Description)));

        await userManager.AddToRoleAsync(user, role.Name!);

        return user.Id;
    }
}
