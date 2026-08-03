using SharedKernel;

namespace Identity.Domain;

internal static class UserErrors
{
    public static readonly Error NotFound = Error.NotFound("users.NotFound", "User was not found.");
}
