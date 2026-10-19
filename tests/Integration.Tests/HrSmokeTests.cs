using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Pagination;
using Hr.Contracts;
using Hr.Contracts.Departments;
using Hr.Contracts.Employees;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// B1's verify block: both HR list endpoints must answer a request that names no page at all.
///
/// <para>Declared non-nullable, <c>int pageNumber, int pageSize</c> bound as REQUIRED query
/// parameters, so a bare <c>GET /api/departments</c> died in model binding with a
/// <c>BadHttpRequestException</c> — a 400 raised before the handler, and before the validator that
/// supposedly owns the range, ever ran. That one route backs the departments list, the lookup store
/// and the Add-Employee picker at once.</para>
///
/// <para><b>Both endpoints are asserted, not only the one that was reported.</b>
/// <c>GET /api/employees</c> carried the identical defect; covering just its sibling would have left
/// the same bug live behind a green test.</para>
///
/// <para><b>The page sizes are asserted, not just the status.</b> The two defaults differ on purpose
/// (200 for the departments lookup, 50 for the employees browse list), and a status-only assertion
/// would pass just as happily if both silently collapsed to the same number — or to zero.</para>
///
/// <para>Nothing is asserted about item counts: <c>ApiFixture.ResetAsync</c> is never called, so every
/// class in the collection contributes rows and a count here would be a race, not a fact.</para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class HrSmokeTests(ApiFixture fixture)
{
    [Fact]
    public async Task GetDepartments_WithNoQueryString_Returns200AndTheFirstPage()
    {
        HttpClient client = fixture.CreateClient();
        string token = fixture.IssueTestToken(Guid.NewGuid().ToString(), HrPermissions.DepartmentsRead);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage response = await client.GetAsync("/api/departments");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        PagedResult<DepartmentResponse>? page =
            await response.Content.ReadFromJsonAsync<PagedResult<DepartmentResponse>>();

        Assert.NotNull(page);
        Assert.Equal(1, page!.PageNumber);
        Assert.Equal(200, page.PageSize);
    }

    [Fact]
    public async Task GetEmployees_WithNoQueryString_Returns200AndTheFirstPage()
    {
        HttpClient client = fixture.CreateClient();
        string token = fixture.IssueTestToken(Guid.NewGuid().ToString(), HrPermissions.EmployeesRead);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage response = await client.GetAsync("/api/employees");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        PagedResult<EmployeeProfile>? page =
            await response.Content.ReadFromJsonAsync<PagedResult<EmployeeProfile>>();

        Assert.NotNull(page);
        Assert.Equal(1, page!.PageNumber);
        Assert.Equal(50, page.PageSize);
    }

    /// <summary>
    /// <b>The known gap this used to pin is closed (HR/Identity Phase 0, Aug 0.1).</b>
    ///
    /// <para>This began as B1's positive control: making paging optional must supply a default for the
    /// caller who names no page, without disabling the range check for the caller who names a bad one.
    /// It failed then, and the reason was not B1: <c>ValidationDecorator</c> declared only
    /// <c>CommandHandler&lt;&gt;</c> and <c>CommandHandler&lt;,&gt;</c>, and <c>DecorateQueryHandler</c>
    /// applied the logging decorator and nothing else, so no query validator in any module had ever been
    /// invoked. <c>pageSize=0</c> reached the handler as a literal zero (<c>?? 200</c> cannot help, because
    /// 0 is not null) and answered 200 with an empty page; <c>pageSize=100000</c> was unbounded.</para>
    ///
    /// <para>The test was written to go red the day queries got a validating decorator, and it did.
    /// <c>ModuleCqrsRegistration.DecorateQueryHandler</c> now wraps every query handler in
    /// <c>ValidationDecorator.QueryHandler&lt;,&gt;</c>, so <c>GetDepartmentsQueryValidator</c>'s
    /// <c>PageNumber &gt; 0</c> and <c>InclusiveBetween(1, 200)</c> are enforced at the route, on both
    /// sides of the range. It now asserts what the system should do.</para>
    /// </summary>
    [Theory]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=100000")]
    [InlineData("pageNumber=0")]
    public async Task GetDepartments_ExplicitlyInvalidPaging_IsRejected_ByTheQueryValidator(string paging)
    {
        HttpClient client = fixture.CreateClient();
        string token = fixture.IssueTestToken(Guid.NewGuid().ToString(), HrPermissions.DepartmentsRead);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage response = await client.GetAsync($"/api/departments?{paging}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// B2's verify: the DETAIL route must serve the enriched shape, not the five-field
    /// <c>EmployeeSummary</c> it used to.
    ///
    /// <para>The employee is seeded IN-PROCESS on purpose. <c>HireEmployeeOverHttpAsync</c> reads this
    /// very route back to recover the <c>AppUserId</c>, so seeding through it would make the test's
    /// subject part of its own setup — and, because that helper deserialises into
    /// <c>EmployeeSummary</c>, it keeps passing whatever the new fields do. That is precisely the shape
    /// of a test that stays green while ceasing to prove anything.</para>
    ///
    /// <para>Every assertion below is on a field <c>EmployeeSummary</c> did not carry, because those are
    /// the ones the profile screen was rendering blank. <c>FullName</c> and <c>Email</c> prove the
    /// batched Identity enrichment actually ran — <c>FullName</c> falls back to an empty string when the
    /// user cannot be resolved, so a non-empty name is the liveness check, not decoration.</para>
    /// </summary>
    [Fact]
    public async Task GetEmployeeById_ReturnsTheDetailShape_WithNameAndEmailFromIdentity()
    {
        string email = $"b2-{Guid.NewGuid():N}@example.com";
        (Guid employeeId, _) = await HrTestSeeding.HireEmployeeAsync(
            fixture, email, jobTitle: "Structural Engineer", firstName: "Detail", lastName: "Screen");

        HttpClient client = fixture.CreateClient();
        string token = fixture.IssueTestToken(Guid.NewGuid().ToString(), HrPermissions.EmployeesRead);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage response = await client.GetAsync($"/api/employees/{employeeId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        EmployeeDetailResponse? detail = await response.Content.ReadFromJsonAsync<EmployeeDetailResponse>();
        Assert.NotNull(detail);

        // From Identity, via the one batched GetProfilesAsync call. Blank here means enrichment silently
        // did not happen -- the exact failure B2 exists to fix.
        Assert.NotEmpty(detail!.FullName);
        Assert.Contains("Detail", detail.FullName, StringComparison.Ordinal);
        Assert.Contains("Screen", detail.FullName, StringComparison.Ordinal);
        Assert.Equal(email, detail.Email);

        // From EmployeeResponse -- none of these existed on the old EmployeeSummary response.
        Assert.Equal("Structural Engineer", detail.JobTitle);
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow), detail.HireDate);
        Assert.Equal("FullTime", detail.ContractType);

        Assert.Equal(employeeId, detail.Id);
    }

    /// <summary>
    /// The other half of B2: a missing employee is a ProblemDetails 404 from the query handler's own
    /// <c>EmployeeErrors.NotFound</c>, not the bare empty 404 the route used to return. Asserting the
    /// body, not just the status, is what distinguishes the new path from the old one.
    /// </summary>
    [Fact]
    public async Task GetEmployeeById_ForAnUnknownId_Is404WithAProblemBody()
    {
        HttpClient client = fixture.CreateClient();
        string token = fixture.IssueTestToken(Guid.NewGuid().ToString(), HrPermissions.EmployeesRead);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage response = await client.GetAsync($"/api/employees/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("employees.NotFound", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    /// <summary>
    /// B3's verify: the members route serves <c>EmployeeProfile</c>, not the bare <c>Guid[]</c> it used
    /// to. The id is asserted alongside the name so this proves an ENRICHMENT of the same set rather
    /// than merely that some list came back — a route returning the right names for the wrong
    /// department would otherwise pass.
    /// </summary>
    [Fact]
    public async Task GetDepartmentMembers_ReturnsProfiles_NotBareIds()
    {
        string email = $"b3-{Guid.NewGuid():N}@example.com";
        (Guid employeeId, _) = await HrTestSeeding.HireEmployeeAsync(
            fixture, email, firstName: "Member", lastName: "Row");

        HttpClient client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            fixture.IssueTestToken(
                Guid.NewGuid().ToString(),
                HrPermissions.DepartmentsCreate,
                HrPermissions.DepartmentsAssignToUser,
                HrPermissions.DepartmentsViewUsers));

        HttpResponseMessage created = await client.PostAsJsonAsync(
            "/api/departments", new { Name = $"Structures {Guid.NewGuid():N}"[..24] });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Guid departmentId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("departmentId").GetGuid();

        HttpResponseMessage assigned = await client.PostAsync(
            $"/api/employees/{employeeId}/department/{departmentId}", content: null);
        Assert.Equal(HttpStatusCode.NoContent, assigned.StatusCode);

        HttpResponseMessage response = await client.GetAsync($"/api/departments/{departmentId}/members");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        List<EmployeeProfile>? members = await response.Content.ReadFromJsonAsync<List<EmployeeProfile>>();
        Assert.NotNull(members);

        EmployeeProfile member = Assert.Single(members!);
        Assert.Equal(employeeId, member.Id);
        Assert.Contains("Member", member.FullName, StringComparison.Ordinal);
        Assert.Equal(email, member.Email);
    }

    /// <summary>
    /// B4's verify. <c>emergencyContactName</c> was declared on the private
    /// <c>UpdateEmployeeRequest</c> and never once read — the column behind it was dropped by migration
    /// <c>20260827091145_UpdateEmployeeEntity</c>. A client sending it got no error and no effect: the
    /// value was silently discarded.
    ///
    /// <para>The field no longer exists on the request record, so System.Text.Json's default behaviour
    /// — ignore unrecognised members — takes over. Sending it must still 204, exactly as sending nothing
    /// for it always did: removing a dead field is not a wire-breaking change for any client that was
    /// never being honoured anyway. What proves B4 rather than merely "the PUT still works" is the
    /// second half — the update takes effect (job title changes), which is what silently discarding a
    /// stray field should still allow.</para>
    /// </summary>
    [Fact]
    public async Task UpdateEmployee_WithEmergencyContactNameInTheBody_Is204AndTheRestOfTheUpdateApplies()
    {
        (Guid employeeId, _) = await HrTestSeeding.HireEmployeeAsync(
            fixture, $"b4-{Guid.NewGuid():N}@example.com", jobTitle: "Junior Engineer");

        HttpClient client = fixture.CreateClient();
        string token = fixture.IssueTestToken(
            Guid.NewGuid().ToString(), HrPermissions.EmployeesUpdate, HrPermissions.EmployeesRead);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage response = await client.PutAsJsonAsync($"/api/employees/{employeeId}", new
        {
            JobTitle = "Senior Engineer",
            EmergencyContactName = "Should Be Ignored",
        });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        EmployeeDetailResponse? detail = await client.GetFromJsonAsync<EmployeeDetailResponse>(
            $"/api/employees/{employeeId}");
        Assert.Equal("Senior Engineer", detail!.JobTitle);
    }

    /// <summary>
    /// B5's verify: <c>employees.view_own</c> was declared and registered, guarding no route, so the
    /// Profile screen had no data source. The token here carries the employee's own <c>AppUserId</c> as
    /// its subject — not the employee id, and not a caller with <c>employees.read</c> — the same
    /// distinction B2's DETAIL route draws between "who is asking" and "which record".
    ///
    /// <para>Uses <see cref="EmployeeDetailResponse"/>, the same shape B2 built, on purpose: the two
    /// routes answer the same question for two different callers and must not drift into two different
    /// shapes for it.</para>
    /// </summary>
    [Fact]
    public async Task GetEmployeesMe_ReturnsTheCallersOwnDetailRecord()
    {
        string email = $"b5-{Guid.NewGuid():N}@example.com";
        (Guid employeeId, string appUserId) = await HrTestSeeding.HireEmployeeAsync(
            fixture, email, jobTitle: "Site Engineer", firstName: "Self", lastName: "Service");

        HttpClient client = fixture.CreateClient();
        string token = fixture.IssueTestToken(appUserId, HrPermissions.EmployeesViewOwn);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage response = await client.GetAsync("/api/employees/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        EmployeeDetailResponse? detail = await response.Content.ReadFromJsonAsync<EmployeeDetailResponse>();
        Assert.NotNull(detail);
        Assert.Equal(employeeId, detail!.Id);
        Assert.Equal(email, detail.Email);
        Assert.Equal("Site Engineer", detail.JobTitle);
    }

    /// <summary>
    /// The outcome D5/B5 both call out explicitly: a client user legitimately has no employee record.
    /// That is 404, not 401 and not 403 — the caller authenticated fine and holds the permission; there
    /// is simply no row to answer with. A random authenticated id was never hired, so
    /// <c>ResolveEmployeeIdAsync</c> answers <c>null</c>.
    /// </summary>
    [Fact]
    public async Task GetEmployeesMe_ForACallerWithNoEmployeeRecord_Is404()
    {
        HttpClient client = fixture.CreateClient();
        string token = fixture.IssueTestToken(Guid.NewGuid().ToString(), HrPermissions.EmployeesViewOwn);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage response = await client.GetAsync("/api/employees/me");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
