using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Auditing;
using BuildingBlocks.Outbox;
using Hr.Contracts;
using Hr.Contracts.Employees;
using Identity.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// Identity &amp; HR Phase 3 through the HTTP pipeline:
/// <list type="bullet">
/// <item>audited removes (Aug 3.2);</item>
/// <item>the read endpoints and any-of authorization (N6);</item>
/// <item>unique-index races answering the check's error (Aug 3.5, outcome sets only; the deterministic
/// catch-path tests are in the module test projects);</item>
/// <item>paging and literal ILIKE (Aug 3.4);</item>
/// <item>update validators (Aug 3.6);</item>
/// <item>the name and <c>LoginActive</c> (N7);</item>
/// <item>the moved detail and members reads (Aug 3.10);</item>
/// <item>the password policy (N9).</item>
/// </list>
/// Each test makes its own users, roles and rows: <see cref="ApiFixture"/> is shared and never reset.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class Phase3HttpTests(ApiFixture fixture)
{
    // --- Aug 3.2: audited removes ---

    [Fact]
    public async Task A_grant_a_revoke_and_a_role_claim_removal_each_leave_an_audit_row()
    {
        HttpClient admin = await TestUsers.AdminClientAsync(fixture);
        TestSession target = await TestUsers.CreateUserWithPermissionsAsync(fixture, IdentityPermissions.RolesRead);
        DateTimeOffset started = DateTimeOffset.UtcNow;

        await ExpectAsync(HttpStatusCode.NoContent, await admin.PostAsJsonAsync(
            $"/api/permissions/users/{target.UserId}/overrides/grant", new { Permission = IdentityPermissions.UsersRead }));
        await ExpectAsync(HttpStatusCode.NoContent, await admin.DeleteAsync(
            $"/api/permissions/users/{target.UserId}/overrides/{IdentityPermissions.UsersRead}"));
        await ExpectAsync(HttpStatusCode.NoContent, await admin.DeleteAsync(
            $"/api/permissions/roles/{target.RoleId}/{IdentityPermissions.RolesRead}"));

        List<AuditLog> rows = await AuditRowsSinceAsync(started);
        Assert.Single(rows, a => a.EntityName == "UserPermissionOverride"
            && a.Action == AuditAction.Create && a.NewValues!.Contains(target.UserId));
        Assert.Single(rows, a => a.EntityName == "UserPermissionOverride"
            && a.Action == AuditAction.Delete && a.OldValues!.Contains(target.UserId));
        Assert.Single(rows, a => a.EntityName == "ApplicationRoleClaim"
            && a.Action == AuditAction.Delete && a.OldValues!.Contains(target.RoleId!));
    }

    /// <summary>C2: through the real route, so the remove validator can't refuse a code the catalog no
    /// longer knows.</summary>
    [Fact]
    public async Task A_held_stale_code_is_removable_over_HTTP()
    {
        HttpClient admin = await TestUsers.AdminClientAsync(fixture);
        string roleId = await TestUsers.CreateRoleAsync(admin);
        await ExecuteAsync($"""
            INSERT INTO identity.role_claims ("RoleId", "ClaimType", "ClaimValue", "Group", "DisplayName")
            VALUES ('{roleId}', 'permissions', 'retired.code', 'Retired', 'Retired Code');
            """);

        await ExpectAsync(HttpStatusCode.NoContent, await admin.DeleteAsync($"/api/permissions/roles/{roleId}/retired.code"));

        Assert.Equal(0L, await ScalarAsync($"""SELECT count(*) FROM identity.role_claims WHERE "RoleId" = '{roleId}'"""));
    }

    // --- N6: read endpoints ---

    [Fact]
    public async Task Users_list_pages_searches_literally_and_needs_users_read()
    {
        string tag = Guid.NewGuid().ToString("N")[..10];
        await IdentityTestSeeding.ProvisionActiveUserAsync(fixture, NewEmail(), firstName: $"Pct{tag}50%");
        await IdentityTestSeeding.ProvisionActiveUserAsync(fixture, NewEmail(), firstName: $"Pct{tag}500");
        await IdentityTestSeeding.ProvisionActiveUserAsync(fixture, NewEmail(), firstName: $"Und{tag}a_b");
        await IdentityTestSeeding.ProvisionActiveUserAsync(fixture, NewEmail(), firstName: $"Und{tag}axb");
        string clientEmail = NewEmail();
        await IdentityTestSeeding.ProvisionActiveUserAsync(
            fixture, clientEmail, kind: UserKind.Client, roleName: IdentityRoles.Client);
        HttpClient reader = TokenClient(IdentityPermissions.UsersRead);

        JsonElement percent = await GetAsync(reader, $"/api/users?search={Uri.EscapeDataString($"Pct{tag}50%")}");
        JsonElement underscore = await GetAsync(reader, $"/api/users?search={Uri.EscapeDataString($"Und{tag}a_b")}");
        JsonElement both = await GetAsync(reader, $"/api/users?search=Pct{tag}&pageSize=1&page=2");

        Assert.Equal([$"Pct{tag}50%"], Names(percent));
        Assert.Equal([$"Und{tag}a_b"], Names(underscore));
        Assert.Equal(2, both.GetProperty("totalCount").GetInt32());
        Assert.Single(both.GetProperty("items").EnumerateArray());

        // Admin and clients are part of the list by design.
        JsonElement adminRow = Items(await GetAsync(reader, "/api/users?search=admin@system.dms")).Single();
        Assert.Contains("admin", adminRow.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
        Assert.Equal("Client", Items(await GetAsync(reader, $"/api/users?search={clientEmail}")).Single()
            .GetProperty("userType").GetString());

        Assert.Equal(HttpStatusCode.Forbidden, (await TokenClient(IdentityPermissions.RolesRead).GetAsync("/api/users")).StatusCode);
    }

    /// <summary>B11: the roles come from one join, so a bigger page costs no more statements.</summary>
    [Fact]
    public async Task Users_list_query_count_does_not_grow_with_the_page()
    {
        for (int i = 0; i < 3; i++)
        {
            await IdentityTestSeeding.ProvisionActiveUserAsync(fixture, NewEmail());
        }

        HttpClient reader = TokenClient(IdentityPermissions.UsersRead);
        await GetAsync(reader, "/api/users?pageSize=1"); // warm-up

        fixture.ClearCapturedSql();
        await GetAsync(reader, "/api/users?pageSize=1");
        int small = fixture.CapturedSql.Count;

        fixture.ClearCapturedSql();
        await GetAsync(reader, "/api/users?pageSize=5");
        int large = fixture.CapturedSql.Count;

        Assert.Equal(small, large);
    }

    [Fact]
    public async Task Role_members_are_paged_and_an_unknown_role_is_404()
    {
        HttpClient admin = await TestUsers.AdminClientAsync(fixture);
        string roleId = await TestUsers.CreateRoleAsync(admin);
        foreach (string email in new[] { NewEmail(), NewEmail() })
        {
            string userId = await IdentityTestSeeding.ProvisionActiveUserAsync(fixture, email);
            await ExpectAsync(HttpStatusCode.NoContent, await admin.PostAsync($"/api/roles/{roleId}/users/{userId}", null));
        }

        HttpClient reader = TokenClient(IdentityPermissions.RolesRead);
        JsonElement page = await GetAsync(reader, $"/api/roles/{roleId}/users?pageSize=1");

        Assert.Equal(2, page.GetProperty("totalCount").GetInt32());
        Assert.Single(Items(page));
        Assert.Equal(HttpStatusCode.NotFound, (await reader.GetAsync($"/api/roles/{Guid.NewGuid()}/users")).StatusCode);
    }

    [Fact]
    public async Task A_user_reads_their_own_roles_but_not_someone_elses_unless_they_hold_roles_read()
    {
        TestSession self = await TestUsers.CreateUserInRoleAsync(fixture, IdentityRoles.Employee);
        TestSession other = await TestUsers.CreateUserInRoleAsync(fixture, IdentityRoles.Employee);

        HttpResponseMessage own = await self.Client.GetAsync($"/api/roles/users/{self.UserId}");
        HttpResponseMessage theirs = await self.Client.GetAsync($"/api/roles/users/{other.UserId}");
        HttpResponseMessage asReader = await TokenClient(IdentityPermissions.RolesRead).GetAsync($"/api/roles/users/{other.UserId}");

        await ExpectAsync(HttpStatusCode.OK, own);
        Assert.Contains(IdentityRoles.Employee, (await own.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray()
            .Select(r => r.GetProperty("name").GetString()));
        Assert.Equal(HttpStatusCode.NotFound, theirs.StatusCode);
        await ExpectAsync(HttpStatusCode.OK, asReader);
    }

    /// <summary>B1: any-of passes with each listed permission alone and fails with neither.</summary>
    [Fact]
    public async Task The_catalog_is_open_to_roles_read_or_permissions_manage()
    {
        Assert.Equal(HttpStatusCode.OK, (await TokenClient(IdentityPermissions.RolesRead).GetAsync("/api/permissions")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await TokenClient(IdentityPermissions.PermissionsManage).GetAsync("/api/permissions")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await TokenClient(IdentityPermissions.UsersRead).GetAsync("/api/permissions")).StatusCode);
    }

    // --- Aug 3.5: races, outcome sets ---

    [Fact]
    public async Task Two_concurrent_hires_with_one_email_give_201_and_DuplicatedEmail()
    {
        HttpClient hr = HrClient();
        string email = NewEmail();

        HttpResponseMessage[] responses = await Task.WhenAll(
            hr.PostAsJsonAsync("/api/employees", NewEmployeeBody(email)),
            hr.PostAsJsonAsync("/api/employees", NewEmployeeBody(email)));

        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.BadRequest], responses.Select(r => r.StatusCode).Order());
        Assert.Contains("users.DuplicatedEmail", await responses.Single(r => r.StatusCode == HttpStatusCode.BadRequest)
            .Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Two_concurrent_department_creates_with_one_name_give_200_and_400()
    {
        HttpClient hr = HrClient();
        string name = $"Race {Guid.NewGuid():N}"[..20];

        HttpResponseMessage[] responses = await Task.WhenAll(
            hr.PostAsJsonAsync("/api/departments", new { Name = name }),
            hr.PostAsJsonAsync("/api/departments", new { Name = name }));

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.BadRequest], responses.Select(r => r.StatusCode).Order());
    }

    // --- Aug 3.4: paging and ILIKE ---

    [Fact]
    public async Task Employee_pages_are_stable_on_ties_and_a_huge_page_is_empty_not_a_500()
    {
        HttpClient hr = HrClient();
        string title = $"Tie{Guid.NewGuid():N}"[..20];
        for (int i = 0; i < 5; i++)
        {
            await HireAsync(jobTitle: title);
        }

        List<Guid> seen = [];
        for (int page = 1; page <= 3; page++)
        {
            JsonElement result = await GetAsync(hr, $"/api/employees?jobTitleFilter={title}&pageSize=2&pageNumber={page}");
            seen.AddRange(Items(result).Select(e => e.GetProperty("id").GetGuid()));
            Assert.Equal(5, result.GetProperty("totalCount").GetInt32());
        }

        JsonElement pastTheEnd = await GetAsync(hr, $"/api/employees?jobTitleFilter={title}&pageSize=2&pageNumber=4");
        JsonElement huge = await GetAsync(hr, $"/api/employees?jobTitleFilter={title}&pageSize=200&pageNumber={int.MaxValue}");

        Assert.Equal(5, seen.Distinct().Count());
        Assert.Empty(Items(pastTheEnd));
        Assert.Empty(Items(huge));
        Assert.Equal(5, huge.GetProperty("totalCount").GetInt32());
    }

    /// <summary>B10, against Postgres: "50%" matches "50%" and not "500"; "_" is literal too.</summary>
    [Fact]
    public async Task Job_title_and_department_filters_match_wildcards_literally()
    {
        HttpClient hr = HrClient();
        string tag = Guid.NewGuid().ToString("N")[..8];
        await HireAsync(jobTitle: $"Lit{tag}50%");
        await HireAsync(jobTitle: $"Lit{tag}500");
        await CreateDepartmentAsync(hr, $"Dlit{tag}a_b");
        await CreateDepartmentAsync(hr, $"Dlit{tag}axb");

        JsonElement employees = await GetAsync(hr, $"/api/employees?jobTitleFilter={Uri.EscapeDataString($"Lit{tag}50%")}");
        JsonElement departments = await GetAsync(hr, $"/api/departments?nameFilter={Uri.EscapeDataString($"lit{tag}a_b")}");

        Assert.Equal([$"Lit{tag}50%"], Items(employees).Select(e => e.GetProperty("jobTitle").GetString()));
        Assert.Single(Items(departments));
    }

    // --- Aug 3.6: update validators ---

    [Fact]
    public async Task Update_refuses_a_long_phone_long_notes_and_an_end_date_before_the_hire_date()
    {
        HttpClient hr = HrClient();
        Guid employee = await HireAsync();

        HttpResponseMessage phone = await hr.PutAsJsonAsync($"/api/employees/{employee}", Body(phone: new string('1', 51)));
        HttpResponseMessage notes = await hr.PutAsJsonAsync($"/api/employees/{employee}", Body(notes: new string('n', 2001)));
        HttpResponseMessage endDate = await hr.PutAsJsonAsync($"/api/employees/{employee}",
            Body(endDate: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1)));

        Assert.Equal(HttpStatusCode.BadRequest, phone.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, notes.StatusCode);
        await ExpectProblemAsync(endDate, HttpStatusCode.BadRequest, "employees.EndDateBeforeHireDate");
    }

    // --- N7: name and LoginActive ---

    [Fact]
    public async Task A_PUT_with_a_name_renames_the_login_and_LoginActive_follows_deactivation()
    {
        HttpClient hr = HrClient();
        Guid employee = await HireAsync();
        await ProcessOutboxAsync();

        await ExpectAsync(HttpStatusCode.NoContent, await hr.PutAsJsonAsync($"/api/employees/{employee}",
            Body(firstName: " Renamed ", lastName: "Person")));
        EmployeeDetailResponse renamed = (await hr.GetFromJsonAsync<EmployeeDetailResponse>($"/api/employees/{employee}"))!;

        await ExpectAsync(HttpStatusCode.NoContent, await hr.PostAsync($"/api/employees/{employee}/deactivate", null));
        await ProcessOutboxAsync();
        EmployeeDetailResponse deactivated = (await hr.GetFromJsonAsync<EmployeeDetailResponse>($"/api/employees/{employee}"))!;

        Assert.Equal("Renamed Person", renamed.FullName);
        Assert.True(renamed.LoginActive);
        Assert.False(deactivated.LoginActive);
    }

    [Fact]
    public async Task A_PUT_with_only_one_name_part_is_400()
    {
        HttpClient hr = HrClient();
        Guid employee = await HireAsync();

        Assert.Equal(HttpStatusCode.BadRequest,
            (await hr.PutAsJsonAsync($"/api/employees/{employee}", Body(firstName: "Only"))).StatusCode);
    }

    // --- N9 ---

    [Fact]
    public async Task A_weak_password_is_WeakPassword_not_a_vague_provisioning_failure()
    {
        HttpResponseMessage response = await HrClient().PostAsJsonAsync("/api/employees", NewEmployeeBody(NewEmail(), "alllowercase1!"));

        await ExpectProblemAsync(response, HttpStatusCode.BadRequest, "employees.WeakPassword");
    }

    // --- Aug 3.10: moved reads ---

    [Fact]
    public async Task Members_of_an_unknown_department_and_me_for_a_non_employee_are_ProblemDetails_404s()
    {
        HttpClient members = TokenClient(HrPermissions.DepartmentsViewUsers);
        HttpClient me = TokenClient(HrPermissions.EmployeesViewOwn);

        HttpResponseMessage unknownDepartment = await members.GetAsync($"/api/departments/{Guid.NewGuid()}/members");
        HttpResponseMessage notAnEmployee = await me.GetAsync("/api/employees/me");

        await ExpectProblemAsync(unknownDepartment, HttpStatusCode.NotFound, "departments.NotFound");
        await ExpectProblemAsync(notAnEmployee, HttpStatusCode.NotFound, "employees.NotFound");
        Assert.Equal("application/problem+json", notAnEmployee.Content.Headers.ContentType?.MediaType);
    }

    // --- helpers ---

    private HttpClient TokenClient(params string[] permissions)
    {
        HttpClient client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", fixture.IssueTestToken(Guid.NewGuid().ToString(), permissions));
        return client;
    }

    private HttpClient HrClient() => TokenClient(
        HrPermissions.EmployeesCreate, HrPermissions.EmployeesRead, HrPermissions.EmployeesUpdate,
        HrPermissions.EmployeesSetActive, HrPermissions.DepartmentsRead, HrPermissions.DepartmentsCreate);

    private async Task<Guid> HireAsync(string jobTitle = "Engineer") =>
        (await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, NewEmail(), jobTitle, "Test", "User")).EmployeeId;

    private static async Task CreateDepartmentAsync(HttpClient hr, string name) =>
        await ExpectAsync(HttpStatusCode.OK, await hr.PostAsJsonAsync("/api/departments", new { Name = name }));

    private static object Body(
        string? phone = null, string? notes = null, DateOnly? endDate = null, string? firstName = null, string? lastName = null) => new
        {
            JobTitle = "Engineer",
            ContractType = "FullTime",
            PhoneNumber = phone,
            EmergencyContactPhone = (string?)null,
            Notes = notes,
            EndDate = endDate,
            FirstName = firstName,
            LastName = lastName,
        };

    private static object NewEmployeeBody(string email, string password = IdentityTestSeeding.Password) => new
    {
        email,
        password,
        FirstName = "Race",
        LastName = "Hire",
        JobTitle = "Engineer",
        HireDate = DateOnly.FromDateTime(DateTime.UtcNow),
        ContractType = "FullTime",
    };

    private static async Task<JsonElement> GetAsync(HttpClient client, string url)
    {
        HttpResponseMessage response = await client.GetAsync(url);
        await ExpectAsync(HttpStatusCode.OK, response);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static IEnumerable<JsonElement> Items(JsonElement page) => page.GetProperty("items").EnumerateArray();

    private static string[] Names(JsonElement page) => [.. Items(page).Select(u => u.GetProperty("firstName").GetString()!)];

    private async Task ProcessOutboxAsync() =>
        await fixture.Services.GetServices<IHostedService>().OfType<OutboxProcessor>().Single()
            .ProcessPendingAsync(CancellationToken.None);

    /// <summary>The value columns are jsonb, so the content checks run in memory over this test's rows.</summary>
    private async Task<List<AuditLog>> AuditRowsSinceAsync(DateTimeOffset since)
    {
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SharedDbContext>().AuditLogs.AsNoTracking()
            .Where(a => a.Timestamp >= since).ToListAsync();
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private async Task<object?> ScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }

    private static string NewEmail() => $"p3-{Guid.NewGuid():N}@example.com";

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
