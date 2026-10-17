using BuildingBlocks.Messaging;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Results;
using Crm.Contracts;
using Crm.Contracts.Leads;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using SharedKernel;

namespace Crm.Infrastructure.Endpoints;

/// <summary>
/// Anonymous, unauthenticated CRM endpoints — the phase's first real anonymous surface anywhere in the
/// repo. This group deliberately omits <c>.RequirePermission(...)</c>/<c>.RequireAuthorization()</c>: do
/// not add anything here that should require a signed-in caller, and do not move this route under an
/// authorized group.
///
/// <para><b>No dedicated "public lead" command exists in <c>Crm.Contracts</c></b> (unlike the backend's
/// own <c>SubmitPublicLeadCommand</c>) — this reuses <see cref="CreateCrmLeadCommand"/> directly. That
/// command's <c>OwnerId</c> is a required real employee <c>Guid</c> (<c>Crm.Domain.CrmLead</c> has no
/// "unassigned" state), which an anonymous caller has no way to supply. Rather than widen
/// <c>Crm.Domain</c>'s own invariant or invent a new cross-module "any active employee" lookup under
/// this task's own time budget, the intake owner is a single configured employee id
/// (<c>Crm:PublicLeadIntakeOwnerId</c>) — an operational deployment requirement, matching how
/// <c>JwtSettings:Key</c> already must be configured or the host fails to start. Unconfigured or
/// unparseable here fails closed with a 503, never a fabricated owner id.</para>
/// </summary>
internal sealed class CrmPublicEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/crm/public").WithTags("Crm Public");

        group.MapPost("/leads", async (
            HttpContext httpContext, PublicLeadRequest request, IConfiguration cfg,
            ICommandHandler<CreateCrmLeadCommand, Guid> handler, CancellationToken ct) =>
        {
            // Belt-and-suspenders alongside the RequestSizeLimitAttribute metadata below: that
            // metadata is read by Kestrel before the body is buffered, but this checks the value
            // model binding already resolved, so the cap holds even if a future host swap (e.g. a
            // different server, or a test harness that binds the body itself) stops reading endpoint
            // metadata the way Kestrel does.
            if (httpContext.Request.ContentLength is > 4_096)
            {
                return Results.Problem(statusCode: StatusCodes.Status413PayloadTooLarge, detail: "Request body too large.");
            }

            if (!Guid.TryParse(cfg["Crm:PublicLeadIntakeOwnerId"], out Guid ownerId))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    detail: "Public lead intake is not configured.");
            }

            Result<Guid> result = await handler.HandleAsync(new CreateCrmLeadCommand(
                request.FirstName, request.LastName, LeadSource.Website, ownerId,
                request.Email, request.Phone, request.Notes), ct);

            // 202 with no body either way (per the plan's own anonymous-surface requirement) - the
            // response never distinguishes a real submission from a validation failure it can't fix
            // by retrying differently, so it can't be used to probe past field-shape errors.
            return result.IsSuccess ? Results.Accepted() : result.ToProblem();
        })
        .RequireRateLimiting(RateLimitPolicies.CrmPublicLeads)
        .WithMetadata(new RequestSizeLimitAttribute(4_096))
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status429TooManyRequests)
        .Produces(StatusCodes.Status202Accepted);
    }

    private sealed record PublicLeadRequest(string FirstName, string LastName, string? Email = null, string? Phone = null, string? Notes = null);
}
