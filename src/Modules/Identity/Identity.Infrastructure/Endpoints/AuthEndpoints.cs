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
/// Logs in, refreshes, revokes, changes password and logs out — five routes, all of them things a person does to
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
///
/// <para>K7: the host's <c>FallbackPolicy</c> requires an authenticated user on any endpoint that declares
/// nothing, so leaving authorization off no longer makes a route anonymous. Login, refresh and revoke
/// carry an explicit <c>.AllowAnonymous()</c>, and Rule 12 holds the list.</para>
///
/// <para>Phase 1B: all five share the IP-keyed <see cref="RateLimitPolicies.AuthLogin"/> window.</para>
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
        }).AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.AuthLogin);

        group.MapPost("/refresh", async (
            RefreshTokenRequest request, ICommandHandler<RefreshTokenCommand, AuthResponse> handler, CancellationToken ct) =>
        {
            Result<AuthResponse> result = await handler.HandleAsync(new RefreshTokenCommand(request), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.AuthLogin);

        group.MapPost("/revoke", async (
            RefreshTokenRequest request, ICommandHandler<RevokeRefreshTokenCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new RevokeRefreshTokenCommand(request), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.AuthLogin);

        group.MapPost("/change-password", async (
            ChangePasswordRequest request, ICommandHandler<ChangePasswordCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new ChangePasswordCommand(request), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequireAuthorization().RequireRateLimiting(RateLimitPolicies.AuthLogin);

        // K5: ends every session of the caller (all refresh tokens, and live access tokens via the
        // security stamp). Authenticated, no permission: it only ever acts on the caller's own account.
        group.MapPost("/logout", async (ICommandHandler<LogoutCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new LogoutCommand(), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequireAuthorization().RequireRateLimiting(RateLimitPolicies.AuthLogin);
    }
}
