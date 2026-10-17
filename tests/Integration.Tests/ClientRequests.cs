namespace Integration.Tests;

/// <summary>
/// The request body for <c>POST /api/clients</c>. Creating a client now creates their portal login too,
/// so the body carries an email and a password where it used to carry an <c>AppUserId</c> the caller had
/// provisioned separately.
/// </summary>
internal static class ClientRequests
{
    public static object NewClientBody(string emailPrefix, string address = "1 Client Rd") => new
    {
        Email = $"{emailPrefix}-{Guid.NewGuid():N}@example.com",
        password = IdentityTestSeeding.Password,
        FirstName = "Portal",
        LastName = "Client",
        address,
    };
}
