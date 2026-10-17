using BuildingBlocks.Messaging;
using Hr.Contracts;
using Hr.Contracts.Employees;
using Identity.Contracts;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;
using Xunit;

namespace Projects.Application.Tests;

/// <summary>
/// Hires an employee through HR's real command. <c>CreateEmployeeCommand</c> now creates the login as
/// well, so the two-step "provision a user, then quote its id" dance every fixture used to perform has
/// collapsed into this one call — and the <c>AppUserId</c>, which the caller used to choose, has to be
/// read back out through <c>IHrDirectory</c>.
///
/// <para>The new login is created inactive and is activated here, which is the state a drained outbox
/// would have left. Fixtures want a working employee; the inactive window is asserted deliberately by
/// <c>Hr.Application.Tests.EmployeeFlowTests</c> and must not be papered over there.</para>
///
/// <para>Duplicated per test project, for the reason <c>CrmTestSeeding</c> already records: these test
/// projects do not reference each other.</para>
/// </summary>
internal static class HrTestSeeding
{
    // The enum name as a string, matching CreateEmployeeCommand.ContractType - Hr.Contracts cannot
    // reference Hr.Domain, so the wire shape is the name rather than the enum.
    private const string FullTime = "FullTime";

    public static async Task<(Guid EmployeeId, string AppUserId)> HireEmployeeAsync(
        IServiceProvider sp,
        string email,
        string jobTitle = "Engineer",
        string firstName = "Test",
        string lastName = "User")
    {
        Result<Guid> hired = await sp.GetRequiredService<ICommandHandler<CreateEmployeeCommand, Guid>>()
            .HandleAsync(new CreateEmployeeCommand(
                email,
                IdentityTestSeeding.Password,
                firstName,
                lastName,
                jobTitle,
                DateOnly.FromDateTime(DateTime.UtcNow),
                FullTime));

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
