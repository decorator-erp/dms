using Microsoft.Extensions.DependencyInjection;

namespace Projects.Application.Tests;

/// <summary>
/// A real <c>crm.clients</c> row for <c>Project.ClientId</c>'s FK to reference, created through CRM's
/// real command handler.
///
/// <para>No longer takes an <c>appUserId</c>: <c>CreateClientCommand</c> creates the portal login
/// itself, so a client is now made from an email address rather than from a user someone else
/// provisioned. See <see cref="CrmClientSeeding"/>, which this delegates to.</para>
/// </summary>
internal static class CrmTestSeeding
{
    public static async Task<Guid> SeedClientAsync(IServiceProvider sp, string? emailPrefix = null)
    {
        (Guid clientId, _) = await CrmClientSeeding.CreateClientAsync(
            sp, $"{emailPrefix ?? "seeded-client"}-{Guid.NewGuid():N}@example.com", address: "1 Client Rd");

        return clientId;
    }
}
