using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Auditing;
using BuildingBlocks.Events;
using BuildingBlocks.Outbox;
using BuildingBlocks.Pagination;
using Hr.Contracts;
using Hr.Contracts.Departments;
using Hr.Contracts.Employees;
using Identity.Contracts.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// HR Phase 2 through the HTTP pipeline:
/// <list type="bullet">
/// <item>K1/K2: memberships and heads;</item>
/// <item>K3: inactive filters;</item>
/// <item>HR-1: activation restores the login, in either outbox order;</item>
/// <item>N7: the full-replacement PUT;</item>
/// <item>N8: department names and descriptions;</item>
/// <item>Aug 2.8: phone and notes stay out of the audit trail.</item>
/// </list>
/// Each test hires its own employees and creates its own departments: <see cref="ApiFixture"/> is shared
/// and never reset.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class HrDataIntegrityTests(ApiFixture fixture)
{
    // --- K1/K2: memberships and heads ---

    [Fact]
    public async Task AssignMoveAndMoveBack_RestoresTheFormerMembership_WithANewStart()
    {
        HttpClient hr = HrClient();
        Guid a = await CreateDepartmentAsync(hr);
        Guid b = await CreateDepartmentAsync(hr);
        Guid employee = await HireAsync();

        await ExpectAsync(HttpStatusCode.NoContent, await hr.PostAsync($"/api/employees/{employee}/department/{a}", null));
        DateTime firstStart = await StartedAtAsync(employee, a);
        await ExpectAsync(HttpStatusCode.NoContent, await hr.PostAsync($"/api/employees/{employee}/department/{b}/move", null));
        await ExpectAsync(HttpStatusCode.NoContent, await hr.PostAsync($"/api/employees/{employee}/department/{a}/move", null));

        // One row per pair, restored rather than duplicated; only the current stint's start moved.
        Assert.Equal(2L, await ScalarAsync("""SELECT count(*) FROM hr.employee_departments WHERE "EmployeeId" = @e""", employee));
        Assert.Equal(1L, await ScalarAsync("""SELECT count(*) FROM hr.employee_departments WHERE "EmployeeId" = @e AND "IsActive" """, employee));
        Assert.True(await StartedAtAsync(employee, a) > firstStart);
        Assert.Equal(a, (await DetailAsync(hr, employee)).PrimaryDepartmentId);
    }

    [Fact]
    public async Task AssigningSomeoneAlreadyInAnotherDepartment_Is409_Not500()
    {
        HttpClient hr = HrClient();
        Guid a = await CreateDepartmentAsync(hr);
        Guid b = await CreateDepartmentAsync(hr);
        Guid employee = await HireAsync();
        await ExpectAsync(HttpStatusCode.NoContent, await hr.PostAsync($"/api/employees/{employee}/department/{a}", null));

        HttpResponseMessage second = await hr.PostAsync($"/api/employees/{employee}/department/{b}", null);

        await ExpectProblemAsync(second, HttpStatusCode.Conflict, "employees.AlreadyInAnotherDepartment");
    }

    /// <summary>A6: both requests pass the "not in a department" check; the partial unique index stops
    /// the second, and its 23505 is answered with the same 409, not a 500.</summary>
    [Fact]
    public async Task TwoConcurrentAssignments_OneWins_TheOtherIs409()
    {
        HttpClient hr = HrClient();
        Guid a = await CreateDepartmentAsync(hr);
        Guid b = await CreateDepartmentAsync(hr);
        Guid employee = await HireAsync();

        HttpResponseMessage[] responses = await Task.WhenAll(
            HrClient().PostAsync($"/api/employees/{employee}/department/{a}", null),
            HrClient().PostAsync($"/api/employees/{employee}/department/{b}", null));

        Assert.Equal(
            [HttpStatusCode.NoContent, HttpStatusCode.Conflict],
            responses.Select(r => r.StatusCode).Order());
        Assert.Equal(1L, await ScalarAsync("""SELECT count(*) FROM hr.employee_departments WHERE "EmployeeId" = @e AND "IsActive" """, employee));
    }

    [Fact]
    public async Task RemovingTwice_Is400_AndRemovingTheHead_Is409()
    {
        HttpClient hr = HrClient();
        Guid department = await CreateDepartmentAsync(hr);
        Guid member = await HireAsync();
        Guid head = await HireAsync();
        await ExpectAsync(HttpStatusCode.NoContent, await hr.PostAsync($"/api/employees/{member}/department/{department}", null));
        await ExpectAsync(HttpStatusCode.NoContent, await hr.PostAsync($"/api/departments/{department}/head/{head}", null));

        await ExpectAsync(HttpStatusCode.NoContent, await hr.DeleteAsync($"/api/employees/{member}/department/{department}"));
        HttpResponseMessage again = await hr.DeleteAsync($"/api/employees/{member}/department/{department}");
        HttpResponseMessage removeHead = await hr.DeleteAsync($"/api/employees/{head}/department/{department}");

        await ExpectProblemAsync(again, HttpStatusCode.BadRequest, "departments.UserNotInDepartment");
        await ExpectProblemAsync(removeHead, HttpStatusCode.Conflict, "departments.RemoveHeadFirst");
    }

    [Fact]
    public async Task DeactivatingAHead_ClearsTheHeadship()
    {
        HttpClient hr = HrClient();
        Guid department = await CreateDepartmentAsync(hr);
        Guid head = await HireAsync();
        await ExpectAsync(HttpStatusCode.NoContent, await hr.PostAsync($"/api/departments/{department}/head/{head}", null));

        await ExpectAsync(HttpStatusCode.NoContent, await hr.PostAsync($"/api/employees/{head}/deactivate", null));

        DepartmentResponse after = (await hr.GetFromJsonAsync<DepartmentResponse>($"/api/departments/{department}"))!;
        Assert.Null(after.DepartmentHeadId);
        Assert.Equal(0, after.MemberCount);
    }

    [Fact]
    public async Task DeletingADepartment_EndsItsMemberships_AndClearsItsHead()
    {
        HttpClient hr = HrClient();
        Guid department = await CreateDepartmentAsync(hr);
        Guid member = await HireAsync();
        Guid head = await HireAsync();
        await ExpectAsync(HttpStatusCode.NoContent, await hr.PostAsync($"/api/employees/{member}/department/{department}", null));
        await ExpectAsync(HttpStatusCode.NoContent, await hr.PostAsync($"/api/departments/{department}/head/{head}", null));

        await ExpectAsync(HttpStatusCode.NoContent, await hr.DeleteAsync($"/api/departments/{department}"));

        Assert.Equal(0L, await ScalarAsync("""SELECT count(*) FROM hr.employee_departments WHERE "DepartmentId" = @e AND "IsActive" """, department));
        Assert.Equal(DBNull.Value, await ScalarAsync("""SELECT "DepartmentHeadId" FROM hr.departments WHERE "Id" = @e""", department));
        // Both former members can join another department now.
        Guid other = await CreateDepartmentAsync(hr);
        await ExpectAsync(HttpStatusCode.NoContent, await hr.PostAsync($"/api/employees/{member}/department/{other}", null));
        // Deleted means gone: deleting again is 404 (DepartmentAlreadyDeleted is unreachable now).
        await ExpectProblemAsync(await hr.DeleteAsync($"/api/departments/{department}"), HttpStatusCode.NotFound, "departments.NotFound");
    }

    [Fact]
    public async Task AnInactiveEmployee_CannotBeAssigned_Moved_OrMadeHead()
    {
        HttpClient hr = HrClient();
        Guid department = await CreateDepartmentAsync(hr);
        Guid employee = await HireAsync();
        await ExpectAsync(HttpStatusCode.NoContent, await hr.PostAsync($"/api/employees/{employee}/deactivate", null));

        await ExpectProblemAsync(await hr.PostAsync($"/api/departments/{department}/head/{employee}", null), HttpStatusCode.Conflict, "employees.Inactive");
        await ExpectProblemAsync(await hr.PostAsync($"/api/employees/{employee}/department/{department}", null), HttpStatusCode.Conflict, "employees.Inactive");
        await ExpectProblemAsync(await hr.PostAsync($"/api/employees/{employee}/department/{department}/move", null), HttpStatusCode.Conflict, "employees.Inactive");
    }

    // --- K3: filters ---

    [Fact]
    public async Task Lists_HideInactiveByDefault_AndShowThemWithIncludeInactive()
    {
        HttpClient hr = HrClient();
        string tag = $"Filter{Guid.NewGuid():N}"[..20];
        Guid department = await CreateDepartmentAsync(hr, $"{tag} Dept");
        Guid employee = await HireAsync(jobTitle: tag);
        await ExpectAsync(HttpStatusCode.NoContent, await hr.DeleteAsync($"/api/departments/{department}"));
        await ExpectAsync(HttpStatusCode.NoContent, await hr.PostAsync($"/api/employees/{employee}/deactivate", null));

        PagedResult<DepartmentResponse> departments = (await hr.GetFromJsonAsync<PagedResult<DepartmentResponse>>($"/api/departments?nameFilter={tag}"))!;
        PagedResult<DepartmentResponse> allDepartments = (await hr.GetFromJsonAsync<PagedResult<DepartmentResponse>>($"/api/departments?nameFilter={tag}&includeInactive=true"))!;
        PagedResult<EmployeeProfile> employees = (await hr.GetFromJsonAsync<PagedResult<EmployeeProfile>>($"/api/employees?jobTitleFilter={tag}"))!;
        PagedResult<EmployeeProfile> allEmployees = (await hr.GetFromJsonAsync<PagedResult<EmployeeProfile>>($"/api/employees?jobTitleFilter={tag}&includeInactive=true"))!;

        Assert.DoesNotContain(departments.Items, d => d.Id == department);
        Assert.Contains(allDepartments.Items, d => d.Id == department && !d.IsActive);
        Assert.DoesNotContain(employees.Items, e => e.Id == employee);
        Assert.Contains(allEmployees.Items, e => e.Id == employee && !e.IsActive);
    }

    // --- HR-1: activation restores the login ---

    [Fact]
    public async Task DeactivateProcessActivateProcess_LoginWorks()
    {
        HttpClient hr = HrClient();
        string email = NewEmail();
        Guid employee = await HireAsync(email);

        await ExpectAsync(HttpStatusCode.NoContent, await hr.PostAsync($"/api/employees/{employee}/deactivate", null));
        await ProcessOutboxAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(email)).StatusCode);

        await ExpectAsync(HttpStatusCode.NoContent, await hr.PostAsync($"/api/employees/{employee}/activate", null));
        await ProcessOutboxAsync();

        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(email)).StatusCode);
    }

    /// <summary>Both events delivered after both changes: each handler checks the row first, so the
    /// late deactivation does not undo the activation.</summary>
    [Fact]
    public async Task DeactivateActivateThenProcessOnce_LoginWorks()
    {
        HttpClient hr = HrClient();
        string email = NewEmail();
        Guid employee = await HireAsync(email);

        await ExpectAsync(HttpStatusCode.NoContent, await hr.PostAsync($"/api/employees/{employee}/deactivate", null));
        await ExpectAsync(HttpStatusCode.NoContent, await hr.PostAsync($"/api/employees/{employee}/activate", null));
        await ProcessOutboxAsync();

        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(email)).StatusCode);
    }

    /// <summary>The deactivation event arriving after the activation's has been handled: an outbox retry
    /// of a delivery whose acknowledgement was lost, or a processor that reordered them. The handler reads
    /// the employee first, sees an active employee, and leaves the login alone.</summary>
    [Fact]
    public async Task ALateDeactivationEvent_AfterActivation_LeavesTheLoginWorking()
    {
        HttpClient hr = HrClient();
        string email = NewEmail();
        Guid employee = await HireAsync(email);

        await ExpectAsync(HttpStatusCode.NoContent, await hr.PostAsync($"/api/employees/{employee}/deactivate", null));
        await ExpectAsync(HttpStatusCode.NoContent, await hr.PostAsync($"/api/employees/{employee}/activate", null));
        await ProcessOutboxAsync();

        await using (AsyncServiceScope scope = fixture.Services.CreateAsyncScope())
        {
            // A new EventId, so the inbox doesn't drop it as a duplicate: this is the delivery the
            // state check exists for.
            IIntegrationEventHandler<EmployeeDeactivatedIntegrationEvent> handler = scope.ServiceProvider
                .GetServices<IIntegrationEventHandler<EmployeeDeactivatedIntegrationEvent>>().Single();
            await handler.HandleAsync(
                new EmployeeDeactivatedIntegrationEvent(Guid.NewGuid(), employee, DateTimeOffset.UtcNow),
                CancellationToken.None);
        }

        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(email)).StatusCode);
    }

    /// <summary>A9: re-hiring is activation. Creating a new employee for the same person fails on the
    /// email, because their login still exists.</summary>
    [Fact]
    public async Task Reactivation_IsTheRehirePath()
    {
        HttpClient hr = HrClient();
        string email = NewEmail();
        Guid employee = await HireAsync(email);
        await ExpectAsync(HttpStatusCode.NoContent, await hr.PostAsync($"/api/employees/{employee}/deactivate", null));
        await ProcessOutboxAsync();

        HttpResponseMessage rehire = await hr.PostAsJsonAsync("/api/employees", NewEmployeeBody(email, "Engineer"));
        await ExpectProblemAsync(rehire, HttpStatusCode.BadRequest, "users.DuplicatedEmail");

        await ExpectAsync(HttpStatusCode.NoContent, await hr.PostAsync($"/api/employees/{employee}/activate", null));
        await ProcessOutboxAsync();
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(email)).StatusCode);
    }

    // --- N7 + ContractType: the employee PUT ---

    [Theory]
    [InlineData("99")]
    [InlineData("0")]
    [InlineData("-1")]
    public async Task UpdatingWithAnUndefinedContractType_Is400(string contractType)
    {
        HttpClient hr = HrClient();
        Guid employee = await HireAsync();

        HttpResponseMessage response = await hr.PutAsJsonAsync($"/api/employees/{employee}", FullBody(contractType: contractType));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("FullTime", (await DetailAsync(hr, employee)).ContractType);
    }

    [Fact]
    public async Task UpdatingAnInactiveEmployee_Is409()
    {
        HttpClient hr = HrClient();
        Guid employee = await HireAsync();
        await ExpectAsync(HttpStatusCode.NoContent, await hr.PostAsync($"/api/employees/{employee}/deactivate", null));

        await ExpectProblemAsync(await hr.PutAsJsonAsync($"/api/employees/{employee}", FullBody()), HttpStatusCode.Conflict, "employees.Inactive");
    }

    [Fact]
    public async Task APutWithANullPhone_ClearsIt()
    {
        HttpClient hr = HrClient();
        Guid employee = await HireAsync();
        await ExpectAsync(HttpStatusCode.NoContent, await hr.PutAsJsonAsync($"/api/employees/{employee}", FullBody(phone: "+1 555 0100")));
        Assert.Equal("+1 555 0100", (await DetailAsync(hr, employee)).PhoneNumber);

        await ExpectAsync(HttpStatusCode.NoContent, await hr.PutAsJsonAsync($"/api/employees/{employee}", FullBody(phone: null)));

        Assert.Null((await DetailAsync(hr, employee)).PhoneNumber);
    }

    // --- N8: departments ---

    [Fact]
    public async Task ACasingOnlyRename_IsNotADuplicateOfItself()
    {
        HttpClient hr = HrClient();
        string name = $"Ops {Guid.NewGuid():N}"[..20];
        Guid department = await CreateDepartmentAsync(hr, name);

        HttpResponseMessage rename = await hr.PutAsJsonAsync($"/api/departments/{department}", new { Name = name.ToUpperInvariant(), Description = (string?)null });

        Assert.Equal(HttpStatusCode.NoContent, rename.StatusCode);
    }

    [Fact]
    public async Task ADeletedDepartmentsName_CanBeReused_ButTwoActiveCannotShareOne()
    {
        HttpClient hr = HrClient();
        string name = $"Reuse {Guid.NewGuid():N}"[..20];
        Guid first = await CreateDepartmentAsync(hr, name);

        HttpResponseMessage duplicate = await hr.PostAsJsonAsync("/api/departments", new { Name = name });
        await ExpectProblemAsync(duplicate, HttpStatusCode.BadRequest, "departments.DuplicatedName");

        await ExpectAsync(HttpStatusCode.NoContent, await hr.DeleteAsync($"/api/departments/{first}"));
        HttpResponseMessage reuse = await hr.PostAsJsonAsync("/api/departments", new { Name = name });

        // The create route answers 200 with the id (it always has); the point is it is not refused.
        Assert.Equal(HttpStatusCode.OK, reuse.StatusCode);
    }

    [Fact]
    public async Task ADescriptionOver2000Characters_Is400()
    {
        HttpClient hr = HrClient();

        HttpResponseMessage create = await hr.PostAsJsonAsync("/api/departments", new { Name = $"Long {Guid.NewGuid():N}"[..20], Description = new string('d', 2001) });

        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
    }

    [Fact]
    public async Task ThePutReplacesTheDescription_AndEmptyClearsIt()
    {
        HttpClient hr = HrClient();
        string name = $"Desc {Guid.NewGuid():N}"[..20];
        Guid department = await CreateDepartmentAsync(hr, name, "first");

        await ExpectAsync(HttpStatusCode.NoContent, await hr.PutAsJsonAsync($"/api/departments/{department}", new { Name = name, Description = "" }));

        Assert.Null((await hr.GetFromJsonAsync<DepartmentResponse>($"/api/departments/{department}"))!.Description);
    }

    [Fact]
    public async Task TheDepartmentList_CarriesTheHeadsName_AndTheMemberCount()
    {
        HttpClient hr = HrClient();
        string name = $"Heads {Guid.NewGuid():N}"[..20];
        Guid department = await CreateDepartmentAsync(hr, name);
        Guid head = await HireAsync(firstName: "Hana", lastName: "Head");
        Guid member = await HireAsync();
        await ExpectAsync(HttpStatusCode.NoContent, await hr.PostAsync($"/api/departments/{department}/head/{head}", null));
        await ExpectAsync(HttpStatusCode.NoContent, await hr.PostAsync($"/api/employees/{member}/department/{department}", null));

        PagedResult<DepartmentResponse> list = (await hr.GetFromJsonAsync<PagedResult<DepartmentResponse>>($"/api/departments?nameFilter={Uri.EscapeDataString(name)}"))!;

        DepartmentResponse row = Assert.Single(list.Items);
        Assert.Equal(2, row.MemberCount);
        Assert.Contains("Hana", row.HeadName);
    }

    // --- Aug 2.8: audit redaction ---

    [Fact]
    public async Task ThePhoneChangesAuditRow_DoesNotContainThePhone()
    {
        HttpClient hr = HrClient();
        Guid employee = await HireAsync();
        const string phone = "+1 555 0177";

        await ExpectAsync(HttpStatusCode.NoContent, await hr.PutAsJsonAsync($"/api/employees/{employee}", FullBody(phone: phone)));

        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        SharedDbContext shared = scope.ServiceProvider.GetRequiredService<SharedDbContext>();
        List<AuditLog> rows = await shared.AuditLogs.AsNoTracking()
            .Where(a => a.EntityName == "Employee" && a.EntityId == employee.ToString() && a.Action == AuditAction.Update)
            .ToListAsync();
        AuditLog row = Assert.Single(rows);
        Assert.DoesNotContain(phone, row.NewValues);
        Assert.Contains("PhoneNumber", row.ChangedColumns);
    }

    // --- helpers ---

    private HttpClient HrClient()
    {
        HttpClient client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.IssueTestToken(
            Guid.NewGuid().ToString(),
            HrPermissions.EmployeesCreate, HrPermissions.EmployeesRead, HrPermissions.EmployeesUpdate,
            HrPermissions.EmployeesSetActive, HrPermissions.DepartmentsRead, HrPermissions.DepartmentsCreate,
            HrPermissions.DepartmentsUpdate, HrPermissions.DepartmentsDelete,
            HrPermissions.DepartmentsAssignDepartmentHead, HrPermissions.DepartmentsUnassignDepartmentHead,
            HrPermissions.DepartmentsAssignToUser, HrPermissions.DepartmentsRemoveFromUser, HrPermissions.DepartmentsMoveUser));
        return client;
    }

    private async Task<Guid> HireAsync(string? email = null, string jobTitle = "Engineer", string firstName = "Test", string lastName = "User") =>
        (await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, email ?? NewEmail(), jobTitle, firstName, lastName)).EmployeeId;

    private static async Task<Guid> CreateDepartmentAsync(HttpClient hr, string? name = null, string? description = null)
    {
        HttpResponseMessage created = await hr.PostAsJsonAsync("/api/departments", new { Name = name ?? $"Dept {Guid.NewGuid():N}"[..20], Description = description });
        await ExpectAsync(HttpStatusCode.OK, created);
        return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("departmentId").GetGuid();
    }

    private static async Task<EmployeeDetailResponse> DetailAsync(HttpClient hr, Guid employeeId) =>
        (await hr.GetFromJsonAsync<EmployeeDetailResponse>($"/api/employees/{employeeId}"))!;

    private static object FullBody(string contractType = "FullTime", string? phone = null) => new
    {
        JobTitle = "Engineer",
        ContractType = contractType,
        PhoneNumber = phone,
        EmergencyContactPhone = (string?)null,
        Notes = (string?)null,
        EndDate = (DateOnly?)null,
    };

    private static object NewEmployeeBody(string email, string jobTitle) => new
    {
        email,
        password = IdentityTestSeeding.Password,
        FirstName = "Re",
        LastName = "Hire",
        JobTitle = jobTitle,
        HireDate = DateOnly.FromDateTime(DateTime.UtcNow),
        ContractType = "FullTime",
    };

    private Task<HttpResponseMessage> LoginAsync(string email) =>
        fixture.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest(email, IdentityTestSeeding.Password));

    private async Task ProcessOutboxAsync() =>
        await fixture.Services.GetServices<IHostedService>().OfType<OutboxProcessor>().Single()
            .ProcessPendingAsync(CancellationToken.None);

    private async Task<DateTime> StartedAtAsync(Guid employeeId, Guid departmentId)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """SELECT "StartedAtUtc" FROM hr.employee_departments WHERE "EmployeeId" = @e AND "DepartmentId" = @d""";
        command.Parameters.AddWithValue("e", employeeId);
        command.Parameters.AddWithValue("d", departmentId);
        return (DateTime)(await command.ExecuteScalarAsync())!;
    }

    private async Task<object?> ScalarAsync(string sql, Guid id)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("e", id);
        return await command.ExecuteScalarAsync();
    }

    private static string NewEmail() => $"hr-p2-{Guid.NewGuid():N}@example.com";

    private static async Task ExpectAsync(HttpStatusCode expected, HttpResponseMessage response) =>
        Assert.True(response.StatusCode == expected,
            $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri?.PathAndQuery}: expected {(int)expected}, "
            + $"got {(int)response.StatusCode}. {await response.Content.ReadAsStringAsync()}");

    private static async Task ExpectProblemAsync(HttpResponseMessage response, HttpStatusCode expected, string code)
    {
        string body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"Expected {(int)expected} {code}, got {(int)response.StatusCode}: {body}");
        Assert.Contains(code, body);
    }
}
