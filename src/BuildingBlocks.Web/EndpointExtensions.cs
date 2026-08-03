using System.Reflection;
using Microsoft.AspNetCore.Routing;

namespace BuildingBlocks.Web;

public static class EndpointExtensions
{
    /// <summary>
    /// Discovers <see cref="IEndpoint"/> implementations in ONE module's assembly and maps them
    /// directly. Ported from <c>../backend/API/Extensions/EndpointExtensions.cs</c>, but scanning per
    /// module assembly rather than the whole app — and deliberately NOT threaded through DI's global
    /// <c>IEnumerable&lt;IEndpoint&gt;</c> collection the way the backend's version is. A shared
    /// collection registered from every module's own <see cref="IModule.Register"/> call would mix
    /// every module's endpoints together the moment more than one module registers, making it
    /// impossible for a module's own <see cref="IModule.MapEndpoints"/> to map only its own. Endpoint
    /// classes are stateless in the existing pattern (route delegates take <c>[FromServices]</c>
    /// parameters resolved per-request, not through the endpoint class's own constructor), so
    /// constructing them directly here needs no DI container at all.
    /// </summary>
    public static IEndpointRouteBuilder MapModuleEndpoints(this IEndpointRouteBuilder app, Assembly assembly)
    {
        var endpointTypes = assembly.DefinedTypes
            .Where(type => type is { IsAbstract: false, IsInterface: false }
                        && typeof(IEndpoint).IsAssignableFrom(type));

        foreach (var type in endpointTypes)
        {
            var endpoint = (IEndpoint)Activator.CreateInstance(type, nonPublic: true)!;
            endpoint.MapEndpoint(app);
        }

        return app;
    }
}
