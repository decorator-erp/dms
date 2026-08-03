namespace Identity.Application.DTOs;

internal sealed record AuthResponse(string Token, string RefreshToken, DateTime Expiration);
