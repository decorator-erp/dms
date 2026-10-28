using Crm.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Crm.Application.Tests;

/// <summary>C-1: client, contact and note messages are client-scoped. A test that is not about scoping
/// acts as an office user holding <c>crm.clients.read_all</c> in its scope; this returns that user's id
/// for the message's <c>UserId</c>.</summary>
internal static class CrmTestAccess
{
    public const string OfficeUserId = "crm-test-office-user";

    public static string Office(IServiceProvider sp)
    {
        TestCurrentUser currentUser = sp.GetRequiredService<TestCurrentUser>();
        currentUser.UserId ??= OfficeUserId;
        currentUser.Grant(CrmPermissions.Clients.ReadAll);
        return currentUser.UserId;
    }
}
