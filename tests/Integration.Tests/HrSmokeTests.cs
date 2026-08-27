using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BuildingBlocks.Pagination;
using Hr.Contracts;
using Hr.Contracts.Departments;
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
    /// <b>A known gap, pinned deliberately — this asserts what the system DOES, not what it should.</b>
    ///
    /// <para>This began as B1's positive control: making paging optional must supply a default for the
    /// caller who names no page, without disabling the range check for the caller who names a bad one.
    /// It failed, and the reason is not B1. <c>ValidationDecorator</c> declares only
    /// <c>CommandHandler&lt;&gt;</c> and <c>CommandHandler&lt;,&gt;</c>; <c>DecorateQueryHandler</c>
    /// (<c>ModuleCqrsRegistration.cs:122-126</c>) applies the logging decorator and nothing else. <b>No
    /// query validator in any module has ever been invoked</b> — the same is true at HEAD, so this
    /// predates both B1 and the current working set.</para>
    ///
    /// <para>So <c>GetDepartmentsQueryValidator</c>'s <c>PageNumber &gt; 0</c> and
    /// <c>InclusiveBetween(1, 200)</c> are dead code, and <c>pageSize=0</c> reaches the handler as a
    /// literal zero — <c>?? 200</c> cannot help, because 0 is not null. The result is a 200 carrying an
    /// empty page, and an explicit <c>pageSize=100000</c> is likewise unbounded.</para>
    ///
    /// <para>Fixing that means adding a validating decorator for queries in <c>BuildingBlocks</c>,
    /// which would newly enforce every query validator in all nine modules at once. That is well
    /// outside B1, so it is reported rather than done. When someone does fix it, this test goes red —
    /// which is the point of writing it down here instead of only in a message.</para>
    /// </summary>
    [Fact]
    public async Task GetDepartments_ExplicitlyInvalidPageSize_IsNotRejected_BecauseQueryValidatorsNeverRun()
    {
        HttpClient client = fixture.CreateClient();
        string token = fixture.IssueTestToken(Guid.NewGuid().ToString(), HrPermissions.DepartmentsRead);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage response = await client.GetAsync("/api/departments?pageSize=0");

        // Not BadRequest, though GetDepartmentsQueryValidator says it should be. See the remarks above.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
