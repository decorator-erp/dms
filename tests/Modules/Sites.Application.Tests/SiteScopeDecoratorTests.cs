using BuildingBlocks.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Projects.Contracts;
using Sites.Contracts;
using Sites.Domain;
using Sites.Contracts.Surveys;
// The eight enum pairs are duplicated between Contracts and Domain by construction (see
// SiteEnumMapping). These tests speak the Contracts vocabulary; SiteErrors comes from Domain.
using SiteItemDataType = Sites.Contracts.SiteItemDataType;
using Xunit;

namespace Sites.Application.Tests;

/// <summary>
/// <b>Proves the scope decorator is APPLIED, not merely registered.</b>
///
/// <para>This is the one test in S1-06 that cannot be replaced by an inspection. Wiring a scope
/// decorator is a scan-and-decorate loop, and scanning zero types succeeds silently: no exception, no
/// warning, a green build, and every scoped message running with no access control whatsoever. That is
/// exactly how G5-07 shipped <c>ProjectScopeRegistration</c> pointed at the wrong assembly. A
/// registration-shaped assertion ("the extension was called", "the type is in the container") cannot
/// tell the two apart. Only behaviour can: a caller who is not entitled to the site's project must get
/// <c>NotFound</c> from a query whose handler, called directly, would have returned the site.</para>
///
/// <para><b>Confirmed to fail without the decorator.</b> With
/// <c>services.AddSiteScopeDecorator(...)</c> commented out of <c>SitesModule.Register</c>,
/// <see cref="AQueryForAProjectTheCallerCannotSee_IsNotFound"/> fails by returning the site — the
/// handler runs unguarded and answers. That negative run is what makes this test meaningful; it is
/// recorded here because the failing state cannot be committed.</para>
///
/// <para>404, never 403, is itself the contract: three distinct cases — nonsense id, soft-deleted site,
/// someone else's project — must be indistinguishable to a caller who cannot see the project, or the
/// error becomes an existence oracle.</para>
/// </summary>
[Collection(SitesApplicationCollection.Name)]
public sealed class SiteScopeDecoratorTests(SitesApplicationFixture fixture)
{
    private const string Insider = "user-with-read-all";
    private const string Outsider = "user-with-nothing";

    private async Task<Guid> SeedSiteAsync()
    {
        // CaptureValueAsync builds the whole graph a site needs -- project, employee, module, record.
        var itemId = await SeedItemAsync();
        var context = await SiteTestData.CaptureValueAsync(fixture, itemId);
        return context.SiteId;
    }

    private async Task<Guid> SeedItemAsync()
    {
        await using var scope = fixture.CreateScope();
        var result = await scope.ServiceProvider
            .GetRequiredService<ICommandHandler<Contracts.Definition.CreateSiteItemCommand, Guid>>()
            .HandleAsync(new Contracts.Definition.CreateSiteItemCommand(
                new Contracts.Definition.CreateSiteItemRequest(
                    $"w_{Guid.NewGuid():N}"[..20], "Width", "العرض", SiteItemDataType.Number, "cm")));

        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private async Task<SharedKernel.Result<SiteDetailResponse>> ReadAsync(Guid siteId, string userId, bool readAll)
    {
        await using var scope = fixture.CreateScope();

        var currentUser = scope.ServiceProvider.GetRequiredService<TestCurrentUser>();
        currentUser.UserId = userId;
        if (readAll) currentUser.Grant(ProjectsPermissions.Projects.ReadAll);

        return await scope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetSiteByIdQuery, SiteDetailResponse>>()
            .HandleAsync(new GetSiteByIdQuery(siteId, userId));
    }

    /// <summary>
    /// The positive half. Without it the negative test below would pass against a decorator that
    /// refuses everyone — "always 404" is not access control either.
    /// </summary>
    [Fact]
    public async Task AQueryForAProjectTheCallerCanSee_ReturnsTheSite()
    {
        var siteId = await SeedSiteAsync();

        var result = await ReadAsync(siteId, Insider, readAll: true);

        Assert.True(result.IsSuccess);
        Assert.Equal(siteId, result.Value!.Id);
    }

    /// <summary>
    /// <b>The assertion this task exists to make.</b> The outsider holds no permission and is not on any
    /// stage team, so <c>IProjectAccessGuard</c> refuses — and the decorator turns that refusal into
    /// <c>SiteErrors.NotFound</c> before the handler ever runs.
    /// </summary>
    [Fact]
    public async Task AQueryForAProjectTheCallerCannotSee_IsNotFound()
    {
        var siteId = await SeedSiteAsync();

        var result = await ReadAsync(siteId, Outsider, readAll: false);

        Assert.True(result.IsFailure);
        Assert.Equal(SiteErrors.NotFound, result.Error);
        Assert.Equal(404, result.Error.Status);
    }

    /// <summary>
    /// An unresolvable site takes the same branch and the same error as an unauthorized one — the whole
    /// point of collapsing to 404. If these two ever diverge, the response becomes an oracle for whether
    /// a guessed id is real.
    /// </summary>
    [Fact]
    public async Task AQueryForASiteThatDoesNotExist_IsTheSameNotFoundAsAnUnauthorizedOne()
    {
        var missing = await ReadAsync(Guid.NewGuid(), Insider, readAll: true);

        Assert.True(missing.IsFailure);
        Assert.Equal(SiteErrors.NotFound, missing.Error);

        var siteId = await SeedSiteAsync();
        var forbidden = await ReadAsync(siteId, Outsider, readAll: false);

        Assert.Equal(missing.Error, forbidden.Error);
    }
}
