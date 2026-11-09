using BuildingBlocks;
using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Authorization;
using BuildingBlocks.Web.Results;
using Crm.Contracts;
using Crm.Contracts.Leads;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SharedKernel;

namespace Crm.Infrastructure.Endpoints;

/// <summary>Lead CRUD + conversion + intake, governed by <see cref="CrmPermissions.Leads"/>.
/// <see cref="CreateCrmLeadCommand.OwnerId"/> is caller-supplied (an office manager assigning a lead to
/// a specific salesperson) — unlike the note/period/item actor fields elsewhere in this file set, this
/// one is a genuine business choice, not "the current caller", so it is NOT auto-resolved.</summary>
internal sealed class CrmLeadEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder leads = app.MapGroup("/api/crm/leads").WithTags("Crm Leads");

        leads.MapPost("/", async (
            CreateCrmLeadCommand command, ICommandHandler<CreateCrmLeadCommand, Guid> handler, CancellationToken ct) =>
        {
            Result<Guid> result = await handler.HandleAsync(command, ct);
            return result.Match(id => Results.Created($"/api/crm/leads/{id}", new { LeadId = id }), r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Leads.Create);

        leads.MapPut("/{leadId:guid}", async (
            Guid leadId, UpdateLeadRequest request, ICommandHandler<UpdateCrmLeadCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new UpdateCrmLeadCommand(
                leadId, request.FirstName, request.LastName, request.Source, request.OwnerId,
                request.Email, request.Phone, request.Notes), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Leads.Update);

        leads.MapPut("/{leadId:guid}/status", async (
            Guid leadId, ChangeStatusRequest request, ICommandHandler<ChangeCrmLeadStatusCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new ChangeCrmLeadStatusCommand(leadId, request.Status), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Leads.ChangeStatus);

        leads.MapPost("/{leadId:guid}/convert", async (
            Guid leadId, ConvertRequest request, ICurrentUser currentUser,
            ICommandHandler<ConvertCrmLeadCommand, Guid> handler, CancellationToken ct) =>
        {
            Result<string> userId = CrmEndpointActors.RequireUserId(currentUser.UserId);
            if (userId.IsFailure)
            {
                return userId.ToProblem();
            }

            Result<Guid> result = await handler.HandleAsync(new ConvertCrmLeadCommand(leadId, request.ExistingClientId, userId.Value!), ct);
            return result.Match(clientId => Results.Ok(new { ClientId = clientId }), r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Leads.Convert);

        leads.MapPut("/{leadId:guid}/intake", async (
            Guid leadId, CrmLeadIntakeData data, ICommandHandler<SaveCrmLeadIntakeCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new SaveCrmLeadIntakeCommand(leadId, data), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Leads.Update);

        // Same owner and search filters as the list below; one count per status.
        leads.MapGet("/status-counts", async (
            Guid? ownerId, string? search,
            IQueryHandler<GetCrmLeadStatusCountsQuery, CrmLeadStatusCountsResponse> handler, CancellationToken ct) =>
        {
            Result<CrmLeadStatusCountsResponse> result = await handler.HandleAsync(new GetCrmLeadStatusCountsQuery(ownerId, search), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Leads.Read);

        leads.MapGet("/{leadId:guid}/intake", async (
            Guid leadId, IQueryHandler<GetCrmLeadIntakeQuery, CrmLeadIntakeResponse> handler, CancellationToken ct) =>
        {
            Result<CrmLeadIntakeResponse> result = await handler.HandleAsync(new GetCrmLeadIntakeQuery(leadId), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Leads.Read);

        leads.MapGet("/{leadId:guid}", async (
            Guid leadId, IQueryHandler<GetCrmLeadByIdQuery, CrmLeadResponse> handler, CancellationToken ct) =>
        {
            Result<CrmLeadResponse> result = await handler.HandleAsync(new GetCrmLeadByIdQuery(leadId), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Leads.Read);

        leads.MapGet("/", async (
            int? pageNumber, int? pageSize, LeadStatus? status, Guid? ownerId, string? search,
            IQueryHandler<GetCrmLeadsQuery, PagedResult<CrmLeadResponse>> handler, CancellationToken ct) =>
        {
            Result<PagedResult<CrmLeadResponse>> result = await handler.HandleAsync(
                new GetCrmLeadsQuery(pageNumber ?? 1, pageSize ?? 50, status, ownerId, search), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Leads.Read);
    }

    private sealed record UpdateLeadRequest(
        string FirstName, string LastName, LeadSource Source, Guid? OwnerId,
        string? Email, string? Phone, string? Notes);

    private sealed record ChangeStatusRequest(LeadStatus Status);
    private sealed record ConvertRequest(Guid ExistingClientId);
}
