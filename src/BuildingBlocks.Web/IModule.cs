using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Web;

/// <summary>
/// One module's registration contract. The composition root (<c>Api</c>) is the only place that
/// discovers every <see cref="IModule"/> implementation and calls <see cref="Register"/> /
/// <see cref="MapEndpoints"/> — see <see cref="ModuleDiscovery"/> and
/// <see cref="ModuleDependencyOrder"/>.
/// </summary>
public interface IModule
{
    string Name { get; }
    IServiceCollection Register(IServiceCollection services, IConfiguration cfg);
    IEndpointRouteBuilder MapEndpoints(IEndpointRouteBuilder app);
}
