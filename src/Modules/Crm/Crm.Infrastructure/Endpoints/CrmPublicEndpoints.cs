using BuildingBlocks.Messaging;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Results;
using Crm.Contracts;
using Crm.Contracts.Leads;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SharedKernel;

namespace Crm.Infrastructure.Endpoints;

/// <summary>
/// Anonymous, unauthenticated CRM endpoints — the phase's first real anonymous surface anywhere in the
/// repo. This group deliberately omits <c>.RequirePermission(...)</c>/<c>.RequireAuthorization()</c>: do
/// not add anything here that should require a signed-in caller, and do not move this route under an
/// authorized group.
///
/// <para>Since K7 the host has a <c>FallbackPolicy</c> requiring an authenticated user on every endpoint
/// that declares nothing, so "omitting" authorization no longer makes a route anonymous. The route says
/// so explicitly with <c>.AllowAnonymous()</c>, and Rule 12 lists it as one of the four anonymous
/// routes.</para>
///
/// <para>LI-1: the form submits <see cref="SubmitPublicLeadCommand"/> - the client's details and, optionally,
/// their project answers (the lead's intake), stored together. The lead is unassigned: no owner is
/// configured or needed, so the route no longer depends on a <c>Crm:PublicLeadIntakeOwnerId</c> setting
/// (which every deployment that left it unset answered with a 503). The office assigns an owner when it
/// picks the lead up.</para>
/// </summary>
internal sealed class CrmPublicEndpoints : IEndpoint
{
    /// <summary>32 KB: the whole form, project answers included, at the field limits (about 4,400 characters)
    /// in Arabic. A browser sends that as UTF-8, two bytes a character (about 9 KB), but a client whose JSON
    /// writer escapes non-ASCII (System.Text.Json's default) sends six (about 26 KB). It was 4 KB when the
    /// form sent only the client's name and contact details.</summary>
    private const int MaxBodyBytes = 32_768;

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/crm/public").WithTags("Crm Public");

        group.MapPost("/leads", async (
            HttpContext httpContext, PublicLeadRequest request,
            ICommandHandler<SubmitPublicLeadCommand, Guid> handler, CancellationToken ct) =>
        {
            // Belt-and-suspenders alongside the RequestSizeLimitAttribute metadata below: that
            // metadata is read by Kestrel before the body is buffered, but this checks the value
            // model binding already resolved, so the cap holds even if a future host swap (e.g. a
            // different server, or a test harness that binds the body itself) stops reading endpoint
            // metadata the way Kestrel does.
            if (httpContext.Request.ContentLength is > MaxBodyBytes)
            {
                return Results.Problem(statusCode: StatusCodes.Status413PayloadTooLarge, detail: "Request body too large.");
            }

            Result<Guid> result = await handler.HandleAsync(new SubmitPublicLeadCommand(
                request.FirstName, request.LastName, request.Email, request.Phone, request.Notes, request.Intake), ct);

            // 202 with no body on success - the lead's id is never handed to an anonymous caller. A
            // validation failure is a 400 problem, so the form can show which field to fix.
            return result.IsSuccess ? Results.Accepted() : result.ToProblem();
        })
        .AllowAnonymous()
        .RequireRateLimiting(RateLimitPolicies.CrmPublicLeads)
        .WithMetadata(new RequestSizeLimitAttribute(MaxBodyBytes))
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status429TooManyRequests)
        .Produces(StatusCodes.Status202Accepted);
    }

    private sealed record PublicLeadRequest(
        string FirstName, string LastName, string? Email = null, string? Phone = null, string? Notes = null,
        CrmLeadIntakeData? Intake = null);
}
