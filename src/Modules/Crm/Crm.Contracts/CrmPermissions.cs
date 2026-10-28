namespace Crm.Contracts;

/// <summary>
/// Ported verbatim from <c>../backend/Domain/Constants/DefaultPermissions.cs</c>' Clients, Crm,
/// CrmLeads, CrmContacts, CrmPeriods, CrmItems, CrmNotes, CrmDesignProfile, CrmMeetings, CrmArtifacts,
/// CrmBriefs, CrmSpaceTypes, CrmCatalog, CrmSpaces, CrmApprovals and Requirements entries — the actual
/// strings already in use, non-uniform naming and all. Same rule as Phases 2–3: port, don't tidy.
/// <c>crm.design_profile.read</c> keeps its snake_case segment, <c>clients.*</c> keeps its own prefix
/// rather than being folded under <c>crm.</c>, and the client-portal requirement strings keep the
/// third prefix <c>requirements.*</c>. Renaming any of these would silently orphan every role row that
/// already grants them.
///
/// <para><b>56 strings.</b> The Phase 4 brief said 59; the source has 48 <c>crm.*</c> + 6
/// <c>clients.*</c> + the 2 <c>requirements.*</c> the Portal slice needs = 56. The brief's count was a
/// miscount and is corrected here rather than padded to match — see this task's PROGRESS.md entry.
/// <see cref="All"/> is the single list <c>CrmModule</c> registers and the permissions endpoint test
/// counts, so the number can never drift from the constants again.</para>
/// </summary>
public static class CrmPermissions
{
    /// <summary>The client record itself. Deliberately NOT under the <c>crm.</c> prefix — the backend
    /// grants these separately from CRM workflow rights and roles already exist that hold one without
    /// the other.</summary>
    public static class Clients
    {
        public const string Read = "clients.read";
        public const string Update = "clients.update";
        public const string SetActive = "clients.set_active";
        public const string ManageChildren = "clients.manage_children";
        public const string Create = "clients.create";
        public const string Delete = "clients.delete";

        /// <summary>C-1 (LC-M2): sees every client. Without it a client is visible only through a project
        /// the caller can reach, or while the client has no project yet (owner amendment M2). Named under
        /// <c>crm.</c> as the owner decided; it is new, so no existing role row holds it.</summary>
        public const string ReadAll = "crm.clients.read_all";
    }

    /// <summary>Portal shell/nav flag, not a resource gate — holding it grants no data by itself.</summary>
    public const string Portal = "crm.portal";

    public static class Leads
    {
        public const string Read = "crm.leads.read";
        public const string Create = "crm.leads.create";
        public const string Update = "crm.leads.update";
        public const string ChangeStatus = "crm.leads.change_status";
        public const string Convert = "crm.leads.convert";
    }

    public static class Contacts
    {
        public const string Read = "crm.contacts.read";
        public const string Create = "crm.contacts.create";
        public const string Update = "crm.contacts.update";
        public const string SetPrimary = "crm.contacts.set_primary";
    }

    public static class Periods
    {
        public const string Read = "crm.periods.read";
        public const string Open = "crm.periods.open";
        public const string Extend = "crm.periods.extend";
        public const string Close = "crm.periods.close";
    }

    public static class Items
    {
        public const string Read = "crm.items.read";
        public const string Create = "crm.items.create";
        public const string Update = "crm.items.update";
        public const string Send = "crm.items.send";
        public const string Supersede = "crm.items.supersede";
    }

    public static class Notes
    {
        public const string Read = "crm.notes.read";
        public const string Create = "crm.notes.create";
        public const string Update = "crm.notes.update";
        public const string Delete = "crm.notes.delete";
    }

    public static class DesignProfile
    {
        public const string Read = "crm.design_profile.read";
    }

    public static class Meetings
    {
        public const string Read = "crm.meetings.read";
        public const string Schedule = "crm.meetings.schedule";
        public const string RespondSlot = "crm.meetings.respond_slot";
        public const string Reschedule = "crm.meetings.reschedule";
        public const string Cancel = "crm.meetings.cancel";
        public const string Complete = "crm.meetings.complete";
        public const string MarkNoShow = "crm.meetings.mark_no_show";

        /// <summary>Client-side. Held by a portal role, not by office staff.</summary>
        public const string ViewOwn = "crm.meetings.view_own";
        public const string RequestOwn = "crm.meetings.request_own";
        public const string RespondOwn = "crm.meetings.respond_own";
    }

    public static class Artifacts
    {
        public const string Read = "crm.artifacts.read";
        public const string Manage = "crm.artifacts.manage";
    }

    public static class Briefs
    {
        public const string Read = "crm.briefs.read";
        public const string Manage = "crm.briefs.manage";
        public const string Send = "crm.briefs.send";

        /// <summary>Client-side.</summary>
        public const string ViewOwn = "crm.briefs.view_own";
    }

    public static class SpaceTypes
    {
        public const string Read = "crm.space_types.read";
        public const string Manage = "crm.space_types.manage";
    }

    public static class Catalog
    {
        public const string Read = "crm.catalog.read";
        public const string Manage = "crm.catalog.manage";
    }

    public static class Spaces
    {
        public const string Read = "crm.spaces.read";
        public const string Manage = "crm.spaces.manage";
    }

    /// <summary>Client-side approval of CRM artifacts (brief head, gathering entries, requirement
    /// items) — the decide half routes through the Approvals module, these only gate the CRM
    /// surface that lists and submits them.</summary>
    public static class Approvals
    {
        public const string ViewOwn = "crm.approvals.view_own";
        public const string DecideOwn = "crm.approvals.decide_own";
    }

    /// <summary>
    /// The client portal's own requirement strings. A third prefix, neither <c>crm.</c> nor
    /// <c>clients.</c> — ported as found. In scope this phase only because the Portal slice is what
    /// gives <c>ICrmAccessGuard</c>'s client-actor branch a real caller.
    /// </summary>
    public static class Requirements
    {
        public const string ViewOwn = "requirements.view_own";
        public const string SubmitOwn = "requirements.submit_own";
    }

    /// <summary>Every string above, in declaration order. The one list <c>CrmModule</c> registers as
    /// its <c>IModulePermissions</c> set — never re-typed at the registration site.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        Clients.Read, Clients.Update, Clients.SetActive, Clients.ManageChildren, Clients.Create,
        Clients.Delete, Clients.ReadAll,

        Portal,

        Leads.Read, Leads.Create, Leads.Update, Leads.ChangeStatus, Leads.Convert,

        Contacts.Read, Contacts.Create, Contacts.Update, Contacts.SetPrimary,

        Periods.Read, Periods.Open, Periods.Extend, Periods.Close,

        Items.Read, Items.Create, Items.Update, Items.Send, Items.Supersede,

        Notes.Read, Notes.Create, Notes.Update, Notes.Delete,

        DesignProfile.Read,

        Meetings.Read, Meetings.Schedule, Meetings.RespondSlot, Meetings.Reschedule, Meetings.Cancel,
        Meetings.Complete, Meetings.MarkNoShow, Meetings.ViewOwn, Meetings.RequestOwn,
        Meetings.RespondOwn,

        Artifacts.Read, Artifacts.Manage,

        Briefs.Read, Briefs.Manage, Briefs.Send, Briefs.ViewOwn,

        SpaceTypes.Read, SpaceTypes.Manage,

        Catalog.Read, Catalog.Manage,

        Spaces.Read, Spaces.Manage,

        Approvals.ViewOwn, Approvals.DecideOwn,

        Requirements.ViewOwn, Requirements.SubmitOwn,
    ];
}
