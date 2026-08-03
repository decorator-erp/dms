using BuildingBlocks.Messaging;
using Hr.Contracts;
using Hr.Contracts.Employees;
using Identity.Contracts.Auth;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// G3-01: register a user (Identity) → create an employee referencing that user (HR) → assert
/// <see cref="IHrDirectory.ResolveEmployeeIdAsync"/> returns the employee id, and that HR issued no SQL
/// against the <c>identity</c> schema. Runs through the REAL composition root
/// (<see cref="ApiFixture"/>'s <c>WebApplicationFactory&lt;Program&gt;</c>), resolving the public
/// command handlers and <see cref="IHrDirectory"/> directly from its <c>Services</c> — every type
/// touched here (<c>RegisterCommand</c>, <c>CreateEmployeeCommand</c>, <c>IHrDirectory</c>) is public in
/// its module's <c>*.Contracts</c> project, so no <c>InternalsVisibleTo</c> grant is needed to reach any
/// of it, which is itself part of what this test is proving.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class CrossModuleSmokeTests(ApiFixture fixture)
{
    [Fact]
    public async Task Register_then_create_employee_resolves_through_IHrDirectory_with_no_identity_sql()
    {
        using var scope = fixture.Services.CreateScope();
        var sp = scope.ServiceProvider;

        var suffix = Guid.NewGuid().ToString("N");
        var email = $"smoke-{suffix}@example.com";

        var register = sp.GetRequiredService<ICommandHandler<RegisterCommand, string>>();
        var registerResult = await register.HandleAsync(
            new RegisterCommand(new RegisterRequest(email, "P@ssw0rd123!", "Test", "User", null, "employee")));
        Assert.True(registerResult.IsSuccess);
        var appUserId = registerResult.Value!;

        var createEmployee = sp.GetRequiredService<ICommandHandler<CreateEmployeeCommand, Guid>>();
        var createResult = await createEmployee.HandleAsync(
            new CreateEmployeeCommand(appUserId, "Engineer", DateOnly.FromDateTime(DateTime.UtcNow), "FullTime"));
        Assert.True(createResult.IsSuccess);
        var employeeId = createResult.Value;

        // Only the resolve call itself is under observation - registration and employee creation
        // legitimately touch identity (Register writes it directly; CreateEmployee validates AppUserId
        // via IUserDirectory) and would make this assertion meaningless if included.
        fixture.ClearCapturedSql();

        var hrDirectory = sp.GetRequiredService<IHrDirectory>();
        var resolvedId = await hrDirectory.ResolveEmployeeIdAsync(appUserId, CancellationToken.None);

        Assert.Equal(employeeId, resolvedId);

        var touchedIdentity = fixture.CapturedSql.Where(sql => sql.Contains("identity.", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.True(touchedIdentity.Count == 0, $"HR's ResolveEmployeeIdAsync issued SQL touching identity: {string.Join(" | ", touchedIdentity)}");
    }
}
