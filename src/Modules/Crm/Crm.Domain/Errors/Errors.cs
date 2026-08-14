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

internal static class CrmBriefErrors
{
    public static readonly Error NotFound = Error.NotFound("crm.briefs.NotFound", "Design brief was not found.");

    public static readonly Error NotDraft = Error.Conflict(
        "crm.briefs.NotDraft", "Intake can only be seeded onto a Draft head.");

    public static readonly Error NotPending = Error.Conflict(
        "crm.briefs.NotPending", "Only a PendingApproval entry can be approved or rejected.");

    public static readonly Error OnlyLockedSuperseded = Error.Conflict(
        "crm.briefs.OnlyLockedSuperseded", "Only a locked (Approved) entry can be superseded.");

    public static readonly Error EntryLocked = Error.Conflict(
        "crm.briefs.EntryLocked", "This entry is locked and cannot be edited or resent.");

    public static readonly Error EntryPending = Error.Conflict(
        "crm.briefs.EntryPending", "This entry already has a pending approval.");
}

internal static class CrmSpaceErrors
{
    public static readonly Error SpaceNotFound = Error.NotFound("crm.spaces.SpaceNotFound", "Space was not found.");

    public static readonly Error ItemNotFound = Error.NotFound(
        "crm.spaces.ItemNotFound", "Space item was not found.");

    public static readonly Error InvalidData = Error.BadRequest("crm.spaces.InvalidData", "A space name is required.");
}

internal static class CrmSpaceTypeErrors
{
    public static readonly Error CodeRequired = Error.BadRequest("crm.space_types.CodeRequired", "A code is required.");

    public static readonly Error NameRequired = Error.BadRequest(
        "crm.space_types.NameRequired", "English and Arabic names are required.");
}

/// <summary>Shared by <see cref="CrmSpaceItem"/> (G9-04) and the Catalog sub-area (G9-05), which owns
/// most of this error set.</summary>
internal static class CrmCatalogErrors
{
    public static readonly Error ItemNotFound = Error.NotFound("crm.catalog.ItemNotFound", "Catalog item was not found.");

    public static readonly Error CategoryNotFound = Error.NotFound(
        "crm.catalog.CategoryNotFound", "Catalog category was not found.");

    public static readonly Error AttributeNotFound = Error.NotFound(
        "crm.catalog.AttributeNotFound", "Catalog attribute was not found.");

    public static readonly Error CustomNameRequired = Error.BadRequest(
        "crm.catalog.CustomNameRequired", "A name is required for a custom entry.");

    public static readonly Error NotACustomEntry = Error.BadRequest(
        "crm.catalog.NotACustomEntry", "This operation only applies to a custom (non-catalog) entry.");

    public static readonly Error AttributeTypeMismatch = Error.BadRequest(
        "crm.catalog.AttributeTypeMismatch",
        "Exactly one value must be set, matching the attribute's declared data type.");

    public static readonly Error InvalidData = Error.BadRequest(
        "crm.catalog.InvalidData", "A code and both English and Arabic names are required.");

    public static readonly Error SpaceTypeNotFound = Error.NotFound(
        "crm.catalog.SpaceTypeNotFound", "Space type was not found.");
}

internal static class CrmPeriodErrors
{
    public static readonly Error NotFound = Error.NotFound("crm.periods.NotFound", "Requirement period was not found.");

    public static readonly Error NotOpen = Error.Conflict(
        "crm.periods.NotOpen", "This operation requires the period to be open.");

    public static readonly Error InvalidCloseTime = Error.BadRequest(
        "crm.periods.InvalidCloseTime", "The close time must be in the future.");
}

internal static class CrmItemErrors
{
    public static readonly Error NotFound = Error.NotFound(
        "crm.items.NotFound", "Requirement item was not found.");

    public static readonly Error InvalidSource = Error.BadRequest(
        "crm.items.InvalidSource", "The source type must match exactly the one source id that is set.");

    public static readonly Error ItemLocked = Error.Conflict(
        "crm.items.ItemLocked", "This item is locked and cannot be edited or resent.");

    public static readonly Error NotPending = Error.Conflict(
        "crm.items.NotPending", "Only a PendingApproval item can be approved or rejected.");

    public static readonly Error OnlyLockedSuperseded = Error.Conflict(
        "crm.items.OnlyLockedSuperseded", "Only a locked (Approved/NotRequired) item can be superseded.");

    public static readonly Error SendRequiresEmployeeAuthor = Error.BadRequest(
        "crm.items.SendRequiresEmployeeAuthor",
        "Only an employee-authored item can be sent for approval — a client-authored item is already trusted.");
}

internal static class CrmArtifactErrors
{
    public static readonly Error InvalidSubject = Error.BadRequest(
        "crm.artifacts.InvalidSubject",
        "Exactly one of brief, space, requirement item, or meeting must be set.");

    public static readonly Error InvalidAuthor = Error.BadRequest(
        "crm.artifacts.InvalidAuthor", "An artifact must be authored by exactly one of an employee or a contact.");
}

internal static class CrmClientNoteErrors
{
    public static readonly Error NotFound = Error.NotFound("crm.notes.NotFound", "Client note was not found.");
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
