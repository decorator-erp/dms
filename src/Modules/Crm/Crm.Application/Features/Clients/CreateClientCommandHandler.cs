using BuildingBlocks.Messaging;
using BuildingBlocks.Outbox;
using Crm.Contracts;
using Crm.Contracts.Clients;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using Identity.Contracts;
using SharedKernel;

namespace Crm.Application.Features.Clients;

internal sealed class CreateClientCommandValidator : AbstractValidator<CreateClientCommand>
{
    public CreateClientCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);

        // Never attach a custom .WithMessage that interpolates the value of a credential field.
        // Messages become Error.Description and travel to the client.
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8);

        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Address).NotEmpty();
        RuleFor(x => x.PhoneNumber).MaximumLength(50);
    }
}

/// <summary>
/// Creates the portal login and the client row, in that order — the same ordering, for the same reason,
/// as <c>Hr.Application</c>'s <c>CreateEmployeeCommandHandler</c>. Replaces the version that took an
/// existing <c>AppUserId</c> and validated it through <c>IUserDirectory</c>; that check was the
/// application-code stand-in for <c>crm.clients.AppUserId</c>'s own FK, and is unnecessary now that this
/// handler creates the row it used to be checking for.
///
/// <para>This is also the first path in the codebase to store a user as <c>UserType.Client</c>. Every
/// client created before this was recorded as an employee, because self-registration hard-coded
/// <c>UserType.Employee</c>.</para>
/// </summary>
internal sealed class CreateClientCommandHandler(CrmDbContext db, IUserProvisioning provisioning)
    : ICommandHandler<CreateClientCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreateClientCommand command, CancellationToken ct = default)
    {
        // ── THE ORDER BELOW IS THE SECURITY PROPERTY. Do not "tidy" it. ────────────────────────────
        //
        // 1. Provision the portal login INACTIVE. If the client row below never commits, this account
        //    cannot be logged into, and UnlinkedUserReaperJob eventually deletes it.
        Result<string> userResult = await provisioning.CreateAsync(
            new NewUserRequest(
                command.Email,
                command.Password,
                command.FirstName,
                command.LastName,
                command.PhoneNumber,
                UserKind.Client,
                IdentityRoles.Client),
            ct);

        if (userResult.IsFailure)
        {
            return userResult.Error;
        }

        string appUserId = userResult.Value!;

        Result<Client> clientResult = Client.Create(appUserId, command.Address, command.Notes);
        if (clientResult.IsFailure)
        {
            return Result.Failure<Guid>(clientResult.Error);
        }

        Client client = clientResult.Value!;

        // 2. The client row and the event that activates the login commit together, in CrmDbContext's
        //    own transaction.
        db.Clients.Add(client);
        db.Outbox().Enqueue(new ClientCreatedIntegrationEvent(
            Guid.NewGuid(), client.Id, client.AppUserId, DateTimeOffset.UtcNow));

        await db.SaveChangesAsync(ct);

        // 3. Activation happens in ClientCreatedIntegrationEventHandler and NOWHERE else.
        return client.Id;
    }
}
