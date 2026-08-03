using BuildingBlocks.Messaging;

namespace Identity.Contracts.Auth;

/// <summary>
/// The command/query wrapper types and their request/response DTOs for Identity's auth features, moved
/// here from <c>Identity.Application</c> at G1-07. Reason: <c>Identity.Infrastructure</c>'s endpoints
/// need to name these exact types to construct commands from an HTTP request and to resolve
/// <c>ICommandHandler&lt;TCommand,TResponse&gt;</c> from DI — but <c>Identity.Application</c> already
/// has a <c>ProjectReference</c> to <c>Identity.Infrastructure</c> (G1-04, for its own DbContext/JWT
/// services), so the reverse reference Infrastructure would need is a circular project reference MSBuild
/// simply refuses. <c>Identity.Contracts</c> is the one project both sides already reference without a
/// cycle. The validators and handlers implementing these commands are unaffected and stay `internal` in
/// <c>Identity.Application</c> — only the thin wire-shape types moved.
/// </summary>
public sealed record RegisterRequest(
    string Email, string Password, string FirstName, string LastName, string? PhoneNumber, string RoleName);

public sealed record RegisterCommand(RegisterRequest Request) : ICommand<string>;

public sealed record LoginRequest(string Email, string Password);

public sealed record LoginCommand(LoginRequest Request) : ICommand<AuthResponse>;

public sealed record RefreshTokenRequest(string Token, string RefreshToken);

public sealed record RefreshTokenCommand(RefreshTokenRequest Request) : ICommand<AuthResponse>;

public sealed record RevokeRefreshTokenCommand(RefreshTokenRequest Request) : ICommand;

public sealed record ChangePasswordRequest(string UserId, string CurrentPassword, string NewPassword);

public sealed record ChangePasswordCommand(ChangePasswordRequest Request) : ICommand;

public sealed record AuthResponse(string Token, string RefreshToken, DateTime Expiration);
