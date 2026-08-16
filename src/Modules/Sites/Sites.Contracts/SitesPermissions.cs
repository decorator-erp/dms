namespace Sites.Contracts;

/// <summary>
/// Ported verbatim from <c>../backend/Domain/Constants/DefaultPermissions.cs</c>' <c>Sites</c> and
/// <c>SiteDefinitions</c> entries. Port, don't tidy — renaming any of these would silently orphan
/// every role row that already grants them.
///
/// <para><b>7 strings, not 6.</b> <c>PLAN-PHASE5.md:115</c> said six, "verified by grep … No
/// <c>sites.manage</c>". There is indeed no <c>sites.manage</c>, but the pattern used could not match
/// a second dot, so it missed <c>sites.definitions.manage</c> — which the backend keeps in its own
/// <c>SiteDefinitions</c> group and which gates 18 of the 20 definition routes. Shipping six would
/// have left those routes with no correct permission to gate on. Corrected at S1-01; see that entry
/// and the standing rule recorded with it.</para>
///
/// <para><b>The read/write split is the reason the definition layer looked permission-free.</b>
/// Definition <i>reads</i> (module list, <c>/{code}/schema</c>, item list, usage) are gated on
/// <see cref="Read"/>, not on <see cref="Definitions.Manage"/> — the backend's own comment explains
/// why: "the surveyor's capture form cannot render without the schema that shapes it". Only definition
/// <i>writes</i> take <see cref="Definitions.Manage"/>.</para>
///
/// <para><see cref="Approve"/> and <see cref="Delete"/> are office-only. The backend's Employee role
/// holds exactly read/create/update/submit — a surveyor who could approve their own submission would
/// make the Draft → Submitted → Approved lifecycle decorative on day one.</para>
/// </summary>
public static class SitesPermissions
{
    /// <summary>
    /// Covers the site list, detail, records, pivot, grouped, families and BOQ reads — and the
    /// definition reads, per the type-level note above.
    /// </summary>
    public const string Read = "sites.read";

    public const string Create = "sites.create";
    public const string Update = "sites.update";
    public const string Submit = "sites.submit";

    /// <summary>Office only. See the type-level note.</summary>
    public const string Approve = "sites.approve";

    /// <summary>Office only. See the type-level note.</summary>
    public const string Delete = "sites.delete";

    /// <summary>
    /// The backend's separate <c>SiteDefinitions</c> permission group. Kept as a nested class so the
    /// grouping survives the port, even though it holds one string today.
    /// </summary>
    public static class Definitions
    {
        /// <summary>
        /// Writes to the form engine only — create/update modules and items, attach/detach/reorder
        /// links, manage options. The matching reads are gated on <see cref="Read"/>.
        /// </summary>
        public const string Manage = "sites.definitions.manage";
    }

    /// <summary>Every string above, in declaration order. The one list <c>SitesModule</c> registers as
    /// its <c>IModulePermissions</c> set — never re-typed at the registration site.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        Read, Create, Update, Submit, Approve, Delete,

        Definitions.Manage,
    ];
}
