using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Web;

/// <summary>
/// One module's registration contract. The composition root (<c>Api</c>) is the only place that
/// discovers every <see cref="IModule"/> implementation and calls <see cref="Register"/> /
/// <see cref="MapEndpoints"/> / <see cref="SeedAsync"/> — see <see cref="ModuleDiscovery"/> and
/// <see cref="ModuleDependencyOrder"/>.
/// </summary>
public interface IModule
{
    string Name { get; }
    IServiceCollection Register(IServiceCollection services, IConfiguration cfg);
    IEndpointRouteBuilder MapEndpoints(IEndpointRouteBuilder app);

    /// <summary>
    /// Added at G1-07 for Identity's default roles/permissions/admin-user seeding — every future module
    /// with startup data needs the same hook, so it lives on the shared contract rather than being a
    /// one-off Identity concern. Must be idempotent: called once per process start, against a database
    /// that already has this module's rows from a previous run every time except the very first.
    /// A default no-op implementation, so modules with nothing to seed (most of them) don't need to
    /// override it.
    /// </summary>
    Task SeedAsync(IServiceProvider services, CancellationToken ct = default) => Task.CompletedTask;
}
