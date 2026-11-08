using System.Reflection;
using BuildingBlocks.Messaging;
using BuildingBlocks.Web.Authorization;
using Identity.Contracts;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Projects.Contracts;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// P1-A: the claim that makes implied permissions safe. A project access grant implies route codes for the whole
/// user (<see cref="GrantAreaPermissions"/>), so every route one of those codes opens must resolve to one project and
/// be checked against the grant's area: its handler's message implements a scope marker (<c>IProjectScoped</c>,
/// <c>ICrmProjectScoped</c>, <c>ISiteScoped</c>, <c>IDocumentsScoped</c>) or is one of the few handlers that ask the
/// guard themselves (<see cref="CheckedByTheirHandler"/>). Walks the real host's endpoint data source, so a new route
/// mapped with an implied code and no scope fails here, not in production.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class GrantAreaPermissionCoverageTests(ApiFixture fixture)
{
    private static readonly string[] ScopeMarkers =
    [
        "Projects.Contracts.IProjectScoped",
        "Crm.Contracts.ICrmProjectScoped",
        "Sites.Contracts.ISiteScoped",
        "Documents.Contracts.IDocumentsScoped",
    ];

    /// <summary>Messages without a marker whose handlers check the project themselves, each with an area.</summary>
    private static readonly string[] CheckedByTheirHandler =
    [
        "Projects.Contracts.Projects.GetProjectsQuery", // the project list: only reachable projects (ProjectAccessGuard.AccessibleProjectIds)
        "Sites.Contracts.Surveys.GetSitesQuery", // guard, Sites area
        "Sites.Contracts.Surveys.CreateSiteCommand", // guard and level, Sites area
        "Projects.Contracts.Grants.GetProjectAccessGrantsQuery", // GrantRules: never a grantee
        "Projects.Contracts.Grants.CreateProjectAccessGrantCommand",
        "Projects.Contracts.Grants.UpdateProjectAccessGrantCommand",
        "Projects.Contracts.Grants.RevokeProjectAccessGrantCommand",
        // The caller's own assigned work, filtered by the guard; on a grant-only request membership is ignored, and
        // a grantee is never on the granted project (AlreadyMember), so a grant adds nothing to it.
        "Crm.Contracts.MyWork.GetMyGatheringWorkQuery",
    ];

    /// <summary>Global, read-only reference data with no project content, reviewed one by one: the site-survey form
    /// definitions a Sites grantee needs to render a survey, and an item's usage (counts only).</summary>
    private static readonly string[] GlobalReferenceReads =
    [
        "Sites.Contracts.Definition.GetSiteModulesQuery",
        "Sites.Contracts.Definition.GetSiteModuleSchemaQuery",
        "Sites.Contracts.Definition.GetSiteItemsQuery",
        "Sites.Contracts.Definition.GetSiteItemUsageQuery",
    ];

    [Fact]
    public void Every_route_an_implied_code_opens_is_project_scoped()
    {
        RouteEndpoint[] endpoints = [.. fixture.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()];
        List<string> violations = [];
        int covered = 0;
        HashSet<string> codesSeen = [];

        foreach (RouteEndpoint endpoint in endpoints)
        {
            string[] codes =
            [
                .. endpoint.Metadata.OfType<HasPermissionAttribute>().Select(a => a.Permission),
                .. endpoint.Metadata.OfType<HasAnyPermissionAttribute>().SelectMany(a => a.Permissions),
            ];
            string[] implied = [.. codes.Where(GrantAreaPermissions.AllCodes.Contains)];
            if (implied.Length == 0)
            {
                continue;
            }

            covered++;
            codesSeen.UnionWith(implied);
            string route = $"{string.Join(",", endpoint.Metadata.OfType<Microsoft.AspNetCore.Routing.HttpMethodMetadata>().SelectMany(m => m.HttpMethods))} {endpoint.RoutePattern.RawText}";
            Type[] messages = [.. MessagesOf(endpoint)];
            if (messages.Length == 0)
            {
                violations.Add($"{route} [{string.Join(",", implied)}]: no command/query handler to check");
                continue;
            }

            foreach (Type message in messages.Where(m => !IsScoped(m)))
            {
                violations.Add($"{route} [{string.Join(",", implied)}]: {message.FullName} is not project-scoped");
            }
        }

        // LIVENESS: the table opens dozens of routes; an empty walk would pass vacuously.
        Assert.True(covered >= 40, $"expected at least 40 routes behind implied codes, found {covered}");
        Assert.True(violations.Count == 0, "Implied codes open unscoped routes:\n" + string.Join("\n", violations));

        // Every code in the table guards at least one route: a code that opens nothing is a typo or dead weight.
        Assert.Empty(GrantAreaPermissions.AllCodes.Except(codesSeen));
    }

    [Fact]
    public void Every_implied_code_is_in_the_permission_catalog()
    {
        HashSet<string> catalog = [.. fixture.Services.GetRequiredService<IPermissionCatalog>().All().SelectMany(m => m.Permissions)];

        Assert.All(GrantAreaPermissions.AllCodes, code => Assert.Contains(code, catalog));
        Assert.Contains(ProjectsPermissions.Projects.ManageAccess, catalog);
    }

    /// <summary>The command/query types a minimal-API delegate takes a handler for.</summary>
    private static IEnumerable<Type> MessagesOf(RouteEndpoint endpoint) =>
        endpoint.Metadata.OfType<MethodInfo>()
            .SelectMany(m => m.GetParameters())
            .Select(p => p.ParameterType)
            .Where(t => t.IsGenericType && (t.GetGenericTypeDefinition() == typeof(ICommandHandler<>)
                || t.GetGenericTypeDefinition() == typeof(ICommandHandler<,>)
                || t.GetGenericTypeDefinition() == typeof(IQueryHandler<,>)))
            .Select(t => t.GetGenericArguments()[0]);

    private static bool IsScoped(Type message) =>
        message.GetInterfaces().Any(i => ScopeMarkers.Contains(i.FullName))
        || CheckedByTheirHandler.Contains(message.FullName)
        || GlobalReferenceReads.Contains(message.FullName);
}
