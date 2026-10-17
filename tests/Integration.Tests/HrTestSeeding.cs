using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Messaging;
using Hr.Contracts;
using Hr.Contracts.Employees;
using Identity.Contracts;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// Hires an employee through HR's real command, in-process. <c>CreateEmployeeCommand</c> now creates the
/// login as well, so the <c>AppUserId</c> the caller used to supply has to be read back out through
/// <c>IHrDirectory</c>.
///
/// <para>The login is created inactive and is activated here rather than by draining the outbox, so a
/// test that only needs a working employee does not have to care about delivery. Tests that are about
/// the activation path itself — <c>AuditTrailTests</c>' outbox-scope test, and HR's own
/// <c>EmployeeFlowTests</c> — drive the processor and must not use this.</para>
/// </summary>
internal static class HrTestSeeding
{
    /// <summary>
    /// The same thing over real HTTP, for the flows whose point is that the route works. Returns the
    /// <c>AppUserId</c> the route created, read back through <c>GET /api/employees/{id}</c> — the
    /// create response carries only the employee id, and the caller is a different principal from the
    /// person being hired.
    ///
    /// <para>The bootstrap caller is an arbitrary id holding only the two employee permissions. It is
    /// not the new employee, and it is not meant to be: hiring is something one person does to
    /// another.</para>
    /// </summary>
    public static async Task<(Guid EmployeeId, string AppUserId)> HireEmployeeOverHttpAsync(
        ApiFixture fixture,
        string email,
        string jobTitle = "Engineer",
        string firstName = "Test",
        string lastName = "User")
    {
        HttpClient client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            fixture.IssueTestToken(
                Guid.NewGuid().ToString(), HrPermissions.EmployeesCreate, HrPermissions.EmployeesRead));

        HttpResponseMessage created = await client.PostAsJsonAsync("/api/employees", new
        {
            email,
            password = IdentityTestSeeding.Password,
            FirstName = firstName,
            LastName = lastName,
            JobTitle = jobTitle,
            HireDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ContractType = "FullTime",
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        Guid employeeId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("employeeId").GetGuid();

        EmployeeSummary summary = (await client.GetFromJsonAsync<EmployeeSummary>($"/api/employees/{employeeId}"))!;

        await ActivateAsync(fixture, summary.AppUserId);

        return (employeeId, summary.AppUserId);
    }

    /// <summary>Activation belongs to <c>EmployeeHiredIntegrationEventHandler</c> and would happen on
    /// the next outbox sweep. Called directly here because <c>ApiFixture</c> stands the background loops
    /// down, and because draining the outbox mid-setup would also deliver unrelated pending messages
    /// into whatever the test is about to measure. Tests whose subject IS the activation path drive the
    /// processor themselves and do not use these helpers.</summary>
    private static async Task ActivateAsync(ApiFixture fixture, string appUserId)
    {
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        Result activated = await scope.ServiceProvider.GetRequiredService<IUserAdministration>()
            .ReactivateAsync(appUserId, CancellationToken.None);

        Assert.True(activated.IsSuccess, activated.IsFailure ? activated.Error.ToString() : null);
    }

    public static async Task<(Guid EmployeeId, string AppUserId)> HireEmployeeAsync(
        ApiFixture fixture,
        string email,
        string jobTitle = "Engineer",
        string firstName = "Test",
        string lastName = "User")
    {
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IServiceProvider sp = scope.ServiceProvider;

        Result<Guid> hired = await sp.GetRequiredService<ICommandHandler<CreateEmployeeCommand, Guid>>()
            .HandleAsync(new CreateEmployeeCommand(
                email,
                IdentityTestSeeding.Password,
                firstName,
                lastName,
                jobTitle,
                DateOnly.FromDateTime(DateTime.UtcNow),
                "FullTime"));

        Assert.True(hired.IsSuccess, hired.IsFailure ? hired.Error.ToString() : null);

        EmployeeSummary? employee = await sp.GetRequiredService<IHrDirectory>()
            .GetAsync(hired.Value, CancellationToken.None);
        Assert.NotNull(employee);

        Result activated = await sp.GetRequiredService<IUserAdministration>()
            .ReactivateAsync(employee!.AppUserId, CancellationToken.None);
        Assert.True(activated.IsSuccess, activated.IsFailure ? activated.Error.ToString() : null);

        return (hired.Value, employee.AppUserId);
    }
}
