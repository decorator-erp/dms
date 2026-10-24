using BuildingBlocks.Messaging;
using Crm.Contracts;
using Crm.Contracts.Leads;
using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;
using Xunit;

namespace Crm.Application.Tests;

/// <summary>
/// The CRM lead intake review's findings (H1, H2, M1–M5, L1, L2), each pinned through the real DI graph
/// so the validation decorator, the handlers and Postgres all take part. Needs Docker.
/// </summary>
[Collection(CrmApplicationCollection.Name)]
public sealed partial class LeadIntakeReviewTests(CrmApplicationFixture fixture)
{
    // --- helpers ---

    private static async Task<Guid> CreateClientAsync(IServiceProvider sp)
    {
        (Guid clientId, _) = await CrmClientSeeding.CreateClientAsync(
            sp, $"lead-client-{Guid.NewGuid():N}@example.com", address: "1 Convert Way");
        return clientId;
    }

    private static Task<Result<Guid>> ConvertAsync(IServiceProvider sp, Guid leadId, Guid clientId) =>
        sp.GetRequiredService<ICommandHandler<ConvertCrmLeadCommand, Guid>>()
            .HandleAsync(new ConvertCrmLeadCommand(leadId, clientId));

    private async Task<int> CountLeadConvertedEventsAsync(Guid leadId)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        List<string> contents = await scope.ServiceProvider.GetRequiredService<BuildingBlocks.Outbox.SharedDbContext>()
            .OutboxMessages.AsNoTracking()
            .Where(m => m.Type.Contains(nameof(LeadConvertedIntegrationEvent)))
            .Select(m => m.Content)
            .ToListAsync();
        return contents.Count(c => c.Contains(leadId.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The code of a domain error, or the messages of a validation error's entries — the
    /// decorator reports a validator failure as <c>Validation.General</c> with the rule's message.</summary>
    private static IReadOnlyList<string> ErrorCodes(Error error) =>
        error is ValidationError validation
            ? [.. validation.Errors.SelectMany(e => new[] { e.Code, e.Description })]
            : [error.Code];

    private static async Task<Guid> SeedEmployeeAsync(IServiceProvider sp, string label = "lead-owner")
    {
        (Guid employeeId, _) = await HrTestSeeding.HireEmployeeAsync(
            sp, $"{label}-{Guid.NewGuid():N}@example.com", jobTitle: "Sales", lastName: label);
        return employeeId;
    }

    private static async Task<Guid> CreateLeadAsync(
        IServiceProvider sp, string firstName = "Lead", string lastName = "Person", Guid? ownerId = null,
        string? email = null, string? phone = null)
    {
        Guid owner = ownerId ?? await SeedEmployeeAsync(sp);
        Result<Guid> created = await sp.GetRequiredService<ICommandHandler<CreateCrmLeadCommand, Guid>>()
            .HandleAsync(new CreateCrmLeadCommand(firstName, lastName, LeadSource.Website, owner, email, phone));
        Assert.True(created.IsSuccess, created.IsFailure ? created.Error.ToString() : null);
        return created.Value;
    }

    private static Task<Result> SaveIntakeAsync(IServiceProvider sp, Guid leadId, CrmLeadIntakeData data) =>
        sp.GetRequiredService<ICommandHandler<SaveCrmLeadIntakeCommand>>()
            .HandleAsync(new SaveCrmLeadIntakeCommand(leadId, data));

    private static Task<Result<CrmLeadIntakeResponse>> GetIntakeAsync(IServiceProvider sp, Guid leadId) =>
        sp.GetRequiredService<IQueryHandler<GetCrmLeadIntakeQuery, CrmLeadIntakeResponse>>()
            .HandleAsync(new GetCrmLeadIntakeQuery(leadId));

    private async Task<bool> IntakeExistsAsync(Guid leadId)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<CrmDbContext>()
            .CrmLeadIntakes.AnyAsync(i => i.LeadId == leadId);
    }
}
