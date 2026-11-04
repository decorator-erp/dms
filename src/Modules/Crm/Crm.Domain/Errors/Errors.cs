using SharedKernel;

namespace Crm.Domain;

internal static class ClientErrors
{
    public static readonly Error NotFound = Error.NotFound("crm.clients.NotFound", "Client was not found.");

    /// <summary>D-16 / C-F5: the client list's <c>isActive</c> is all, true or false.</summary>
    public static readonly Error InvalidActiveFilter = Error.BadRequest(
        "crm.clients.InvalidActiveFilter", "isActive must be all, true or false.");

    public static readonly Error AppUserIdRequired = Error.BadRequest(
        "crm.clients.AppUserIdRequired", "A linked user id is required.");

    public static readonly Error AddressRequired = Error.BadRequest(
        "crm.clients.AddressRequired", "An address is required.");

    public static readonly Error ChildNotFound = Error.NotFound(
        "crm.clients.ChildNotFound", "The specified child was not found on this client.");

    /// <summary>C-5 (LC-M5): a deactivated client takes no new contacts, notes, children or profile edits.</summary>
    public static readonly Error Inactive = Error.Conflict(
        "crm.clients.Inactive", "This client is deactivated; reactivate it before changing it.");

    /// <summary>C-6 (LC-M6): someone else changed this client (its profile or its children) first.</summary>
    public static readonly Error ConcurrentUpdate = Error.Conflict(
        "crm.clients.ConcurrentUpdate", "The client was changed by someone else. Reload it and try again.");
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

    /// <summary>C-6 (LC-L4): another primary-contact change on the same client won the race.</summary>
    public static readonly Error ConcurrentPrimaryChange = Error.Conflict(
        "crm.contacts.ConcurrentPrimaryChange", "The primary contact was changed at the same time. Reload and try again.");

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

    public static readonly Error ConcurrentUpdate = Error.Conflict(
        "crm.leads.ConcurrentUpdate", "The lead was changed by someone else at the same time. Reload and try again.");

    public static readonly Error OwnerNotFound = Error.BadRequest(
        "crm.leads.OwnerNotFound", "The owner must be an active employee.");

    public static readonly Error ResidentialWithoutSector = Error.BadRequest(
        "crm.leads.ResidentialWithoutSector",
        "Residential intake details can only be sent when the sector is Residential.");
}

internal static class CrmBriefErrors
{
    public static readonly Error NotFound = Error.NotFound("crm.briefs.NotFound", "Design brief was not found.");

    public static readonly Error NotDraft = Error.Conflict(
        "crm.briefs.NotDraft", "Intake can only be seeded onto a Draft head.");

    public static readonly Error UnitDetailsRequired = Error.BadRequest(
        "crm.briefs.UnitDetailsRequired",
        "Unit type and unit condition are required unless the client's converted lead intake supplies them.");

    public static readonly Error NotPending = Error.Conflict(
        "crm.briefs.NotPending", "Only a PendingApproval entry can be approved or rejected.");

    public static readonly Error OnlyLockedSuperseded = Error.Conflict(
        "crm.briefs.OnlyLockedSuperseded", "Only a locked (Approved) entry can be superseded.");

    public static readonly Error EntryLocked = Error.Conflict(
        "crm.briefs.EntryLocked", "This entry is locked and cannot be edited or resent.");

    public static readonly Error EntryPending = Error.Conflict(
        "crm.briefs.EntryPending", "This entry already has a pending approval.");

    public static readonly Error HeadLockedPendingApproval = Error.Conflict(
        "crm.briefs.HeadLockedPendingApproval",
        "The brief head cannot be edited while it is awaiting the client's decision.");

    public static readonly Error NotSendable = Error.Conflict(
        "crm.briefs.NotSendable", "Only a Draft or Rejected brief can be sent for approval.");

    /// <summary>Two sends computed the same round; the unique <c>(BriefId, Round)</c> snapshot index
    /// let exactly one of them through.</summary>
    public static readonly Error ConcurrentSend = Error.Conflict(
        "crm.briefs.ConcurrentSend", "The brief was sent concurrently. Reload and retry.");

    /// <summary>A send kept finding its next round's approval already decided — only reachable through
    /// repeated failed commits; the send gives up rather than loop.</summary>
    public static readonly Error TooManyVoidedRounds = Error.Conflict(
        "crm.briefs.TooManyVoidedRounds", "The send could not open a fresh approval round. Retry later.");

    public static readonly Error ConcurrencyConflict = Error.Conflict(
        "crm.briefs.ConcurrencyConflict", "The brief was modified concurrently. Reload and retry.");

    public static readonly Error StageProjectMismatch = Error.BadRequest(
        "crm.briefs.StageProjectMismatch", "The stage does not belong to this brief's project.");

    public static readonly Error SpaceProjectMismatch = Error.BadRequest(
        "crm.briefs.SpaceProjectMismatch", "The space does not belong to this brief's project.");

    public static readonly Error TaskProjectMismatch = Error.BadRequest(
        "crm.briefs.TaskProjectMismatch", "The task does not belong to this brief's project.");

    public static readonly Error TaskStageMismatch = Error.BadRequest(
        "crm.briefs.TaskStageMismatch", "The task does not belong to this brief's stage.");

    public static readonly Error MeetingProjectMismatch = Error.BadRequest(
        "crm.briefs.MeetingProjectMismatch", "The meeting does not belong to this brief's project.");

    public static readonly Error AlreadySuperseded = Error.Conflict(
        "crm.briefs.AlreadySuperseded", "This entry has already been superseded.");

    public static readonly Error ProjectNotFound = Error.NotFound(
        "crm.briefs.ProjectNotFound", "The project was not found.");

    /// <summary>One brief per (project, stage). Replaces the misleading <see cref="NotDraft"/> a duplicate
    /// create used to return.</summary>
    public static readonly Error AlreadyExists = Error.Conflict(
        "crm.briefs.AlreadyExists", "This stage already has a design brief.");

    public static readonly Error RoundNotFound = Error.NotFound(
        "crm.briefs.RoundNotFound", "The brief has no such round.");

    public static readonly Error SupersedeSpaceMismatch = Error.BadRequest(
        "crm.briefs.SupersedeSpaceMismatch", "A correction must stay on the superseded entry's space.");
}

internal static class CrmSpaceErrors
{
    public static readonly Error SpaceNotFound = Error.NotFound("crm.spaces.SpaceNotFound", "Space was not found.");

    public static readonly Error ItemNotFound = Error.NotFound(
        "crm.spaces.ItemNotFound", "Space item was not found.");

    public static readonly Error InvalidData = Error.BadRequest("crm.spaces.InvalidData", "A space name is required.");

    /// <summary>A-8 (B-M5): a space names a space type that does not exist.</summary>
    public static readonly Error UnknownSpaceType = Error.BadRequest(
        "crm.spaces.UnknownSpaceType", "The space type does not exist.");

    /// <summary>A-7 (B-M4): space names are unique per project, ignoring case.</summary>
    public static readonly Error DuplicateName = Error.Conflict(
        "crm.spaces.DuplicateName", "This project already has a space with that name.");
}

internal static class CrmSpaceTypeErrors
{
    public static readonly Error CodeRequired = Error.BadRequest("crm.space_types.CodeRequired", "A code is required.");

    /// <summary>A-12 (B-L2): an unknown space type is a 404, not <see cref="CodeRequired"/>.</summary>
    public static readonly Error NotFound = Error.NotFound("crm.space_types.NotFound", "Space type was not found.");

    /// <summary>A-7 (B-M4): space type codes are unique.</summary>
    public static readonly Error DuplicateCode = Error.Conflict(
        "crm.space_types.DuplicateCode", "A space type with that code already exists.");

    public static readonly Error NameRequired = Error.BadRequest(
        "crm.space_types.NameRequired", "English and Arabic names are required.");
}

/// <summary>Shared by <see cref="CrmSpaceItem"/> (G9-04) and the Catalog sub-area (G9-05/G9-11), which
/// owns most of this error set.</summary>
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

    public static readonly Error MaxDepthExceeded = Error.BadRequest(
        "crm.catalog.MaxDepthExceeded", "A category tree may not exceed the maximum nesting depth.");

    public static readonly Error CyclicMove = Error.BadRequest(
        "crm.catalog.CyclicMove", "A category cannot be moved under itself or one of its own descendants.");

    public static readonly Error DuplicateMapping = Error.Conflict(
        "crm.catalog.DuplicateMapping", "This category is already mapped to this space type.");
}

internal static class CrmPeriodErrors
{
    public static readonly Error NotFound = Error.NotFound("crm.periods.NotFound", "Requirement period was not found.");

    public static readonly Error NotOpen = Error.Conflict(
        "crm.periods.NotOpen", "This operation requires the period to be open.");

    /// <summary>A-7/A-12 (B-M4, B-L2): a project already has an open period.</summary>
    public static readonly Error AlreadyOpen = Error.Conflict(
        "crm.periods.AlreadyOpen", "This project already has an open requirement period.");

    /// <summary>A-9 (B-M6): an <c>xmin</c> conflict on a period row.</summary>
    public static readonly Error ConcurrentUpdate = Error.Conflict(
        "crm.periods.ConcurrentUpdate", "The period was changed by someone else at the same time. Reload and try again.");

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

    /// <summary>X-1: a portal item named a space type that does not exist or is inactive. A 400 (a bad value
    /// in the request body), distinct from the catalog's own 404 <c>crm.catalog.SpaceTypeNotFound</c>.</summary>
    public static readonly Error UnknownSpaceType = Error.BadRequest(
        "crm.items.UnknownSpaceType", "The space type does not exist or is inactive.");

    /// <summary>A-3 (X-6): an office item belongs to its period's project; a body naming another one is
    /// refused rather than silently corrected.</summary>
    /// <summary>A-10 (B-M7): the item is already out for approval on a live round.</summary>
    public static readonly Error AlreadyPending = Error.Conflict(
        "crm.items.AlreadyPending", "This item is already waiting for the client's decision.");

    /// <summary>A-10: the brief-space bound — a decided-Approved orphan is voided and re-sent at most this
    /// many times in one send.</summary>
    public static readonly Error TooManyVoidedRounds = Error.Conflict(
        "crm.items.TooManyVoidedRounds", "This item's approval was voided too many times. Try again later.");

    /// <summary>A-6 (B-M3): an item is superseded at most once — pre-check plus the partial unique index
    /// on <c>SupersedesItemId</c>.</summary>
    public static readonly Error AlreadySuperseded = Error.Conflict(
        "crm.items.AlreadySuperseded", "This item has already been superseded.");

    /// <summary>A-9 (B-M6): an <c>xmin</c> conflict on an item row.</summary>
    public static readonly Error ConcurrentUpdate = Error.Conflict(
        "crm.items.ConcurrentUpdate", "The item was changed by someone else at the same time. Reload and try again.");

    public static readonly Error ProjectMismatch = Error.BadRequest(
        "crm.items.ProjectMismatch", "The project does not match the period's project.");

    public static readonly Error TaskProjectMismatch = Error.BadRequest(
        "crm.items.TaskProjectMismatch", "The task does not belong to this item's project.");

    public static readonly Error MeetingProjectMismatch = Error.BadRequest(
        "crm.items.MeetingProjectMismatch", "The meeting does not belong to this item's project.");

    public static readonly Error BriefProjectMismatch = Error.BadRequest(
        "crm.items.BriefProjectMismatch", "The brief does not belong to this item's project.");

    public static readonly Error NoteProjectMismatch = Error.BadRequest(
        "crm.items.NoteProjectMismatch", "The note does not belong to this item's project or client.");
}

internal static class CrmArtifactErrors
{
    public static readonly Error InvalidSubject = Error.BadRequest(
        "crm.artifacts.InvalidSubject",
        "Exactly one of brief, space, requirement item, or meeting must be set.");

    public static readonly Error InvalidAuthor = Error.BadRequest(
        "crm.artifacts.InvalidAuthor", "An artifact must be authored by exactly one of an employee or a contact.");

    /// <summary>The entire replacement for the FK structural item 1 drops on
    /// <c>CrmArtifactDocument.DocumentId</c> — see G9-12's own remarks.</summary>
    public static readonly Error DocumentNotFound = Error.NotFound(
        "crm.artifacts.DocumentNotFound", "The referenced document was not found.");
}

internal static class CrmClientNoteErrors
{
    public static readonly Error NotFound = Error.NotFound("crm.notes.NotFound", "Client note was not found.");

    /// <summary>C-8 (LC-I1): only the author, or a <c>crm.notes.manage_all</c> holder, changes a note.</summary>
    public static readonly Error NotAuthor = Error.Forbidden(
        "crm.notes.NotAuthor", "Only the note's author can change or delete it.");

    /// <summary>C-7 (LC-L3): a note's project must be one of its client's projects.</summary>
    public static readonly Error ProjectMismatch = Error.BadRequest(
        "crm.notes.ProjectMismatch", "The note's project does not belong to this client.");
}

/// <summary>The three CRM <c>IGateConditionTypeHandler</c> implementations' own errors (G9-13) —
/// separate from <see cref="CrmBriefErrors"/>/<see cref="CrmPeriodErrors"/>/<see cref="CrmItemErrors"/>
/// since these are shape-validation failures on a gate DEFINITION, not on the CRM entity the gate
/// eventually reads.</summary>
internal static class CrmGateErrors
{
    public static readonly Error UnsupportedOwner = Error.BadRequest(
        "crm.gates.UnsupportedOwner", "This gate condition type only supports a stage-owned gate.");
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

    /// <summary>D-16 / B-F3.</summary>
    public static readonly Error SlotTooLong = Error.BadRequest(
        "crm.meetings.SlotTooLong", "A slot can last at most 480 minutes.");

    public static readonly Error InvalidStateTransition = Error.Conflict(
        "crm.meetings.InvalidStateTransition", "This meeting cannot transition from its current status.");

    public static readonly Error AlreadyStarted = Error.Conflict(
        "crm.meetings.AlreadyStarted", "This meeting has already started.");

    public static readonly Error CancelReasonRequired = Error.BadRequest(
        "crm.meetings.CancelReasonRequired", "A cancellation reason is required.");

    /// <summary>B-8 (M4): the meeting was changed by someone else at the same time.</summary>
    public static readonly Error ConcurrentUpdate = Error.Conflict(
        "crm.meetings.ConcurrentUpdate", "The meeting was changed by someone else at the same time. Reload and try again.");

    /// <summary>B-7 (M3): the slot's start has passed.</summary>
    public static readonly Error SlotExpired = Error.Conflict(
        "crm.meetings.SlotExpired", "This slot's start time has passed. Propose a new time.");

    public static readonly Error SlotNotProposed = Error.Conflict(
        "crm.meetings.SlotNotProposed", "Only a proposed slot can be accepted or declined.");

    /// <summary>M1-01: the backend has this as `SlotNotFound` (404) on the accept/decline path — this
    /// port had only the meeting-level `NotFound`, which read wrong on a slot id.</summary>
    public static readonly Error SlotNotFound = Error.NotFound(
        "crm.meetings.SlotNotFound", "Meeting slot was not found.");

    /// <summary>M1-01: the backend's `NotCounterparty` (403) — the actor accepting/declining a slot, or
    /// rescheduling/canceling a meeting, must be the other side of it (see
    /// `CrmMeetingOrchestration.IsCounterpartyOf`/`IsRequesterOrCounterparty`).</summary>
    public static readonly Error NotCounterparty = Error.Forbidden(
        "crm.meetings.NotCounterparty", "You are not a party to this meeting.");

    /// <summary>M1-01: the backend's `NoSlotsProvided` (400) — `ProposeCrmMeetingCommand` must offer at
    /// least one candidate slot; the entity constructor has nothing to reject an empty list with, since
    /// slots are added by the caller after `CrmMeeting.Create` succeeds.</summary>
    public static readonly Error NoSlotsProvided = Error.BadRequest(
        "crm.meetings.NoSlotsProvided", "At least one slot must be proposed.");
}

/// <summary>G9-14: the Portal's own read/write surface. <see cref="NotFound"/> covers both "no such
/// project" and "a real project that isn't this client's own" — 404-over-403, per <c>CLAUDE.md</c>'s
/// authorization order (a caller who can't see a resource shouldn't learn it exists), matching
/// <c>ProjectScopeDecorator</c>'s own precedent for the identical trade.</summary>
internal static class CrmPortalErrors
{
    public static readonly Error NotFound = Error.NotFound(
        "crm.portal.NotFound", "Nothing was found for that request.");

    public static readonly Error NotAClient = Error.Forbidden(
        "crm.portal.NotAClient", "This action requires a client actor.");

    /// <summary>B1.2: the portal decides only Approved or ChangesRequested.</summary>
    public static readonly Error InvalidDecision = Error.BadRequest(
        "crm.portal.InvalidDecision", "The portal accepts only Approved or ChangesRequested.");

    /// <summary>B1.3: the approvals list takes status=pending or status=decided.</summary>
    public static readonly Error InvalidStatus = Error.BadRequest(
        "crm.portal.InvalidStatus", "Status must be 'pending' or 'decided'.");
}

/// <summary>A-15: CRM's view of a project it does not own.</summary>
internal static class CrmProjectErrors
{
    /// <summary>The project is deactivated: office CRM writes on it are refused (the portal answers 404).</summary>
    public static readonly Error Inactive = Error.Conflict(
        "crm.projects.Inactive", "This project is inactive and takes no new CRM work.");
}
