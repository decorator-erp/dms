using BuildingBlocks.Messaging;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Results;
using Identity.Contracts.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Identity.Infrastructure.Endpoints;

/// <summary>
/// Registers, logs in, refreshes, revokes, and changes password. <c>/refresh</c> and <c>/revoke</c> are
/// deliberately anonymous (no <c>.RequireAuthorization()</c>) — both accept an EXPIRED access token by
/// design (<c>IJwtProvider.ValidateToken(..., validateLifetime: false)</c>); putting the standard JWT
/// bearer authentication middleware in front of them would reject the very expired token they exist to
/// accept, before the handler ever runs.
/// </summary>
internal sealed class AuthEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/register", async (
            RegisterRequest request, ICommandHandler<RegisterCommand, string> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new RegisterCommand(request), ct);
            return result.Match(userId => Results.Ok(new { UserId = userId }), r => r.ToProblem());
        });

        group.MapPost("/login", async (
            LoginRequest request, ICommandHandler<LoginCommand, AuthResponse> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new LoginCommand(request), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        });

        group.MapPost("/refresh", async (
            RefreshTokenRequest request, ICommandHandler<RefreshTokenCommand, AuthResponse> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new RefreshTokenCommand(request), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        });

        group.MapPost("/revoke", async (
            RefreshTokenRequest request, ICommandHandler<RevokeRefreshTokenCommand> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new RevokeRefreshTokenCommand(request), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        });

        group.MapPost("/change-password", async (
            ChangePasswordRequest request, ICommandHandler<ChangePasswordCommand> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new ChangePasswordCommand(request), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequireAuthorization();
    }
}
