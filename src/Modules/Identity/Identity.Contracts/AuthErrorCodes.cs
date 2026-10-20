namespace Identity.Contracts;

/// <summary>Error codes the frontend keys on, kept in one place so the string exists once.</summary>
public static class AuthErrorCodes
{
    /// <summary>N2: the access token predates a change to the account (deactivation, roles, permissions,
    /// password), or its account no longer exists. Refresh or sign in again. The frontend's 006b-1 keys
    /// on this exact string.</summary>
    public const string StaleToken = "authentications.StaleToken";
}
