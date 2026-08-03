using Microsoft.AspNetCore.Routing;

namespace BuildingBlocks.Web;

public interface IEndpoint
{
    void MapEndpoint(IEndpointRouteBuilder app);
}
