using SharedKernel;

namespace Identity.Domain;

internal static class UserErrors
{
    public static readonly Error NotFound = Error.NotFound("users.NotFound", "User was not found.");

    /// <summary>The structural close of the escalation path self-registration left open. Not a
    /// permission check: no caller, however privileged, provisions into a full-access role through
    /// <c>IUserProvisioning</c>. Granting one is a separate, audited role-management operation.</summary>
    public static readonly Error RoleNotAssignableAtProvisioning = Error.BadRequest(
        "users.RoleNotAssignableAtProvisioning",
        "That role cannot be assigned when creating a user.");

    /// <summary>Replaced <c>AuthErrors.DuplicatedEmail</c>, which differed only in its code prefix, and
    /// the prefix was the whole point: provisioning failures surface through <c>POST /api/employees</c>
    /// and <c>POST /api/clients</c>, so a caller reading <c>authentications.*</c> back from an employees
    /// endpoint would be told the wrong thing about where the failure came from.</summary>
    public static Error DuplicatedEmail(string email) =>
        Error.BadRequest("users.DuplicatedEmail", $"Email '{email}' is already registered.");

    /// <summary>Deliberately says nothing. ASP.NET Identity's own messages (password composition rules
    /// it failed, whether the name was taken) are logged, never returned — the caller is a person
    /// creating someone else's account and has no business reading them.</summary>
    public static readonly Error CreateFailed =
        Error.BadRequest("users.CreateFailed", "The user could not be created.");

    public static readonly Error RoleAssignmentFailed =
        Error.BadRequest("users.RoleAssignmentFailed", "The user's role could not be assigned.");
}
