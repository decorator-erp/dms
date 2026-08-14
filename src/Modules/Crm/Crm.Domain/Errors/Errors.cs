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

internal static class CrmLeadErrors
{
    public static readonly Error NotFound = Error.NotFound("crm.leads.NotFound", "Lead was not found.");

    public static readonly Error NameRequired = Error.BadRequest(
        "crm.leads.NameRequired", "First and last name are required.");

    public static readonly Error AlreadyConverted = Error.Conflict(
        "crm.leads.AlreadyConverted", "This lead has already been converted.");

    public static readonly Error InvalidStatusTransition = Error.Conflict(
        "crm.leads.InvalidStatusTransition",
        "Conversion happens only through the convert operation, not a plain status change.");

    /// <summary>Guards the invariant the backend's own <c>CrmLead.Convert</c> left unguarded — the
    /// entity checks its own <c>Guid.Empty</c> shape; the Application handler (G9-09) is what checks
    /// the id names a real, existing client, since Domain has no database access.</summary>
    public static readonly Error InvalidClientId = Error.BadRequest(
        "crm.leads.InvalidClientId", "A client id is required to convert a lead.");

    public static readonly Error DesignerExperienceWithoutUse = Error.BadRequest(
        "crm.leads.DesignerExperienceWithoutUse",
        "Designer experience can only be set when a previous designer was used.");

    public static readonly Error ExperienceReasonWithoutExperience = Error.BadRequest(
        "crm.leads.ExperienceReasonWithoutExperience",
        "An experience reason requires designer experience to be set.");
}

internal static class CrmMeetingErrors
{
    public static readonly Error NotFound = Error.NotFound("crm.meetings.NotFound", "Meeting was not found.");

    public static readonly Error InvalidRequester = Error.BadRequest(
        "crm.meetings.InvalidRequester", "A meeting must be requested by exactly one of an employee or a contact.");

    public static readonly Error InvalidProposer = Error.BadRequest(
        "crm.meetings.InvalidProposer", "A slot must be proposed by exactly one of an employee or a contact.");

    public static readonly Error InvalidSlotRange = Error.BadRequest(
        "crm.meetings.InvalidSlotRange", "A slot must end after it starts and start in the future.");

    public static readonly Error InvalidStateTransition = Error.Conflict(
        "crm.meetings.InvalidStateTransition", "This meeting cannot transition from its current status.");

    public static readonly Error AlreadyStarted = Error.Conflict(
        "crm.meetings.AlreadyStarted", "This meeting has already started.");

    public static readonly Error CancelReasonRequired = Error.BadRequest(
        "crm.meetings.CancelReasonRequired", "A cancellation reason is required.");

    public static readonly Error SlotNotProposed = Error.Conflict(
        "crm.meetings.SlotNotProposed", "Only a proposed slot can be accepted or declined.");
}
