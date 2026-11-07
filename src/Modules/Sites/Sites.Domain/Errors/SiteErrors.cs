using SharedKernel;

namespace Sites.Domain;

/// <summary>
/// Ported from <c>../backend/Domain/Errors/SiteErrors.cs</c>. Two deliberate divergences from the
/// source:
///
/// <para><b>Declared as <c>static readonly</c> fields, not expression-bodied properties.</b> The backend
/// allocates a fresh <c>Error</c> on every access; this repo's own convention (see
/// <c>Crm.Domain.Errors</c>) is a single instance per error. Equality is by record value either way, so
/// <c>Assert.Equal(SiteErrors.X, result.Error)</c> reads the same — this only stops the allocation.</para>
///
/// <para><b><c>BriefProvenanceLost</c> is not ported.</b> Its only caller was
/// <c>GET /api/sites/{siteId}/variance</c>, deferred at S1-01 [D8] because nothing in this system writes
/// <c>Site.SourceBriefId</c>. Porting the error alone would leave a member no code path can produce.
/// Whichever phase makes provenance real adds it back with its route.</para>
/// </summary>
internal static class SiteErrors
{
    private const string Prefix = "sites";

    // ── Definition layer ───────────────────────────────────────────────────────

    public static readonly Error ModuleNotFound = Error.NotFound(
        $"{Prefix}.{nameof(ModuleNotFound)}", "The requested site module was not found.");

    public static readonly Error ItemNotFound = Error.NotFound(
        $"{Prefix}.{nameof(ItemNotFound)}", "The requested site item was not found.");

    public static readonly Error OptionNotFound = Error.NotFound(
        $"{Prefix}.{nameof(OptionNotFound)}", "The requested site item option was not found.");

    public static readonly Error LinkNotFound = Error.NotFound(
        $"{Prefix}.{nameof(LinkNotFound)}", "The item is not linked to this module.");

    public static readonly Error InvalidData = Error.BadRequest(
        $"{Prefix}.{nameof(InvalidData)}", "The supplied site data is not valid.");

    public static readonly Error CodeTaken = Error.Conflict(
        $"{Prefix}.{nameof(CodeTaken)}", "That code is already in use.");

    public static readonly Error AlreadyLinked = Error.Conflict(
        $"{Prefix}.{nameof(AlreadyLinked)}", "The item is already linked to this module.");

    /// <summary>
    /// An <c>xmin</c> optimistic-concurrency conflict — two writers on one row. A retry is the whole
    /// remedy, which is why it is a 409 naming the situation rather than a blanket 500. The backend
    /// declares this but never raises it from the definition layer; S1-06 fixes that (defect B6).
    /// </summary>
    public static readonly Error ConcurrencyConflict = Error.Conflict(
        $"{Prefix}.{nameof(ConcurrencyConflict)}",
        "The site was modified concurrently. Please reload and retry.");

    /// <summary>
    /// Code, data type, unit and tolerance are frozen once any value references the item — they define
    /// how every historical value is read and grouped, so changing one re-shards the family set without
    /// a measurement ever changing.
    /// </summary>
    public static readonly Error ItemLocked = Error.Conflict(
        $"{Prefix}.{nameof(ItemLocked)}",
        "This item has captured values; its code, data type, unit and tolerance can no longer be changed.");

    public static readonly Error ItemInUse = Error.Conflict(
        $"{Prefix}.{nameof(ItemInUse)}", "This item is in use and cannot be deleted. Deactivate it instead.");

    public static readonly Error ModuleInUse = Error.Conflict(
        $"{Prefix}.{nameof(ModuleInUse)}",
        "This module has captured records and cannot be deleted. Deactivate it instead.");

    public static readonly Error SystemModuleNotDeletable = Error.Conflict(
        $"{Prefix}.{nameof(SystemModuleNotDeletable)}", "System modules cannot be deleted.");

    public static readonly Error SystemItemNotDeletable = Error.Conflict(
        $"{Prefix}.{nameof(SystemItemNotDeletable)}", "System items cannot be deleted.");

    /// <summary>
    /// Bounds, length, pattern and tolerance are each legal only on one data type — the domain twin of
    /// <c>ck_site_items_range</c>.
    /// </summary>
    public static readonly Error LimitNotLegalForType = Error.BadRequest(
        $"{Prefix}.{nameof(LimitNotLegalForType)}", "That limit is not valid for this item's data type.");

    public static readonly Error OptionsRequireSelect = Error.BadRequest(
        $"{Prefix}.{nameof(OptionsRequireSelect)}",
        "Only items with the Select data type can carry options.");

    /// <summary>
    /// Renaming a system definition. New in this port: the backend returns
    /// <see cref="SystemModuleNotDeletable"/> from <c>SiteModule.ChangeCode</c> — an error that says
    /// "cannot be deleted" in response to a rename. Fixed rather than ported (S1-03).
    /// </summary>
    public static readonly Error SystemDefinitionNotRenameable = Error.Conflict(
        $"{Prefix}.{nameof(SystemDefinitionNotRenameable)}", "System definitions cannot be renamed.");

    // ── Instance layer ─────────────────────────────────────────────────────────

    public static readonly Error NotFound = Error.NotFound(
        $"{Prefix}.{nameof(NotFound)}", "The requested site was not found.");

    /// <summary>C6: the caller reaches the site's project, but below the level this write needs (Editor to
    /// capture, Manager to decide, reopen or delete).</summary>
    public static readonly Error InsufficientLevel = Error.Forbidden(
        $"{Prefix}.{nameof(InsufficientLevel)}", "Your access level on this project does not allow this action.");

    public static readonly Error RecordNotFound = Error.NotFound(
        $"{Prefix}.{nameof(RecordNotFound)}", "The requested site record was not found.");

    /// <summary>
    /// Dead code in the backend — declared and never raised, because create-site's only guard is an
    /// access check that a caller holding the read-all permission short-circuits without ever touching
    /// the projects table, turning a bad project id into a 500 from the foreign key. S1-07 raises it
    /// (defect B1).
    /// </summary>
    public static readonly Error ProjectNotFound = Error.NotFound(
        $"{Prefix}.{nameof(ProjectNotFound)}", "The requested project was not found.");

    /// <summary>
    /// B-S-M7: a survey cannot be started on a project that is deactivated, Completed or Cancelled.
    /// </summary>
    public static readonly Error ProjectClosed = Error.Conflict(
        $"{Prefix}.{nameof(ProjectClosed)}",
        "The project is inactive or closed; no new site survey can be started on it.");

    public static readonly Error LabelTaken = Error.Conflict(
        $"{Prefix}.{nameof(LabelTaken)}", "Another record in this module already uses that label.");

    /// <summary>
    /// The cross-aggregate rule no database constraint can hold: the space must belong to the site's own
    /// project. Checked in <c>Sites.Application</c> through <c>Crm.Contracts.ICrmSpaceReader</c>, which
    /// exists for this rule alone (S1-01) — a foreign key proves the space exists, never that it belongs
    /// to the right project.
    /// </summary>
    public static readonly Error SpaceNotInProject = Error.BadRequest(
        $"{Prefix}.{nameof(SpaceNotInProject)}", "That space belongs to a different project.");

    public static readonly Error SpaceNotFound = Error.NotFound(
        $"{Prefix}.{nameof(SpaceNotFound)}", "The requested space was not found.");

    /// <summary>
    /// S2: a record's catalog item must exist and be active in CRM. Read through
    /// <c>Crm.Contracts.ICrmCatalogItemReader</c>; the foreign key alone turned a bad id into a 500.
    /// </summary>
    public static readonly Error CatalogItemNotFound = Error.NotFound(
        $"{Prefix}.{nameof(CatalogItemNotFound)}", "The requested catalog item was not found.");

    public static readonly Error InvalidTransition = Error.Conflict(
        $"{Prefix}.{nameof(InvalidTransition)}",
        "That status change is not allowed from the site's current status.");

    /// <summary>
    /// An approved survey is the engineering record; measurements stop moving under it. A
    /// <b>submitted</b> one is frozen for the same reason a step earlier (B12, P5F-02): whoever decides
    /// it must be deciding about the content they were shown. Rejected reopens for rework.
    /// </summary>
    public static readonly Error SiteLocked = Error.Conflict(
        $"{Prefix}.{nameof(SiteLocked)}",
        "This site has been submitted for approval; its records can no longer be changed. "
        + "A rejected site must be reopened for rework before it can be edited.");

    /// <summary>
    /// B-S-L1 (C11) / R1-9: the survey's creator or submitter may not approve it unless they hold the project-wide
    /// override (<c>project.read_all</c>, which the full-access admin and manager roles carry). 403: it is about who
    /// is acting, not about the survey's state.
    /// </summary>
    public static readonly Error SelfApprovalNotAllowed = Error.Forbidden(
        $"{Prefix}.{nameof(SelfApprovalNotAllowed)}",
        "A survey cannot be approved by the person who created or submitted it.");

    public static readonly Error AddressRequiredToSubmit = Error.BadRequest(
        $"{Prefix}.{nameof(AddressRequiredToSubmit)}",
        "A site must have an address before it can be submitted.");

    public static readonly Error ValueTypeMismatch = Error.BadRequest(
        $"{Prefix}.{nameof(ValueTypeMismatch)}", "The supplied value does not match the item's data type.");

    public static readonly Error OptionNotOfItem = Error.BadRequest(
        $"{Prefix}.{nameof(OptionNotOfItem)}", "The chosen option does not belong to that item.");

    public static readonly Error DocumentNotFound = Error.NotFound(
        $"{Prefix}.{nameof(DocumentNotFound)}", "The requested site attachment was not found.");

    /// <summary>
    /// NS2: the document is already attached to this exact subject (the site, or that record). Backed
    /// by <c>ux_site_documents_link</c>, partial on <c>IsActive</c>, so unlinking frees the subject.
    /// </summary>
    public static readonly Error DocumentAlreadyLinked = Error.Conflict(
        $"{Prefix}.{nameof(DocumentAlreadyLinked)}", "That document is already attached here.");

    public static readonly Error InvalidDocumentSubject = Error.BadRequest(
        $"{Prefix}.{nameof(InvalidDocumentSubject)}",
        "An attachment must belong to exactly one of a site or a record.");

    public static readonly Error CodeAllocationFailed = Error.FromException(
        $"{Prefix}.{nameof(CodeAllocationFailed)}", "A site code could not be allocated.");

    public static readonly Error SaveFailed = Error.FromException(
        $"{Prefix}.{nameof(SaveFailed)}", "The site could not be saved.");
}
