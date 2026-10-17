using BuildingBlocks.Messaging;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Results;
using Identity.Contracts.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SharedKernel;

namespace Identity.Infrastructure.Endpoints;

/// <summary>
/// Logs in, refreshes, revokes, and changes password — four routes, all of them things a person does to
/// an account they already have. <b>There is no registration route.</b> Accounts are created by HR
/// (<c>POST /api/employees</c>) and CRM (<c>POST /api/clients</c>), both authenticated and permissioned;
/// the anonymous <c>/register</c> that used to sit here let any caller mint an active account and pick
/// its own role.
///
/// <para><c>/refresh</c> and <c>/revoke</c> are deliberately anonymous (no
/// <c>.RequireAuthorization()</c>) — both accept an EXPIRED access token by design
/// (<c>IJwtProvider.ValidateToken(..., validateLifetime: false)</c>); putting the standard JWT bearer
/// authentication middleware in front of them would reject the very expired token they exist to accept,
/// before the handler ever runs. With <c>/register</c> gone they and <c>/api/crm/public/leads</c> are
/// the whole anonymous surface.</para>
/// </summary>
internal sealed class AuthEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/auth").WithTags("Auth");   

        group.MapPost("/login", async (
            LoginRequest request, ICommandHandler<LoginCommand, AuthResponse> handler, CancellationToken ct) =>
        {
            Result<AuthResponse> result = await handler.HandleAsync(new LoginCommand(request), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        });

        group.MapPost("/refresh", async (
            RefreshTokenRequest request, ICommandHandler<RefreshTokenCommand, AuthResponse> handler, CancellationToken ct) =>
        {
            Result<AuthResponse> result = await handler.HandleAsync(new RefreshTokenCommand(request), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        });

        group.MapPost("/revoke", async (
            RefreshTokenRequest request, ICommandHandler<RevokeRefreshTokenCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new RevokeRefreshTokenCommand(request), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        });

        group.MapPost("/change-password", async (
            ChangePasswordRequest request, ICommandHandler<ChangePasswordCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new ChangePasswordCommand(request), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequireAuthorization();
    }
}
