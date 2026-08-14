using SharedKernel;

namespace Crm.Domain;

internal static class ClientErrors
{
    public static readonly Error NotFound = Error.NotFound("crm.clients.NotFound", "Client was not found.");

    public static readonly Error AppUserIdRequired = Error.BadRequest(
        "crm.clients.AppUserIdRequired", "A linked user id is required.");

    public static readonly Error AddressRequired = Error.BadRequest(
        "crm.clients.AddressRequired", "An address is required.");

    public static readonly Error ChildNotFound = Error.NotFound(
        "crm.clients.ChildNotFound", "The specified child was not found on this client.");
}

internal static class ClientChildErrors
{
    public static readonly Error InvalidGender = Error.BadRequest(
        "crm.client_children.InvalidGender", "Gender must be 'M' or 'F'.");

    public static readonly Error InvalidAge = Error.BadRequest(
        "crm.client_children.InvalidAge", "Age must be between 0 and 120.");
}

internal static class CrmContactErrors
{
    public static readonly Error NotFound = Error.NotFound("crm.contacts.NotFound", "Contact was not found.");

    public static readonly Error NameRequired = Error.BadRequest(
        "crm.contacts.NameRequired", "First and last name are required.");
}
