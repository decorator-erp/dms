namespace Finance.Contracts;

/// <summary>
/// Ported from <c>../backend/Domain/Constants/DefaultPermissions.cs</c>'s nested <c>Finance</c> class
/// (lines 187-200). <b>Twelve strings, recounted rather than trusted.</b>
///
/// <para>An earlier inventory of this area recorded eight. The miss is instructive and is the reason
/// this file carries the note: three of the twelve carry a <i>second</i> dot
/// (<see cref="Variations.Approve"/>/<see cref="Variations.Sign"/>/<see cref="Variations.Apply"/>), and
/// a two-segment grep such as <c>finance\.[a-z_]+</c> truncates all three to <c>finance.variations</c>
/// and collapses them into one match. The recount used <c>finance\.[a-z_.]+</c> with no <c>head</c>
/// truncation. This is the same defect class D1 recorded for Sites.</para>
///
/// <para><b>Why the split exists</b>, per the backend's own comments: <see cref="ManageVariation"/> is
/// the variation <i>authoring</i> bucket — create, submit, reject, cancel — and its string is kept
/// stable, while approve/sign/apply are standalone sensitive transitions (approvals and signatures)
/// that a role may hold independently of authoring.</para>
///
/// <para><b><see cref="Read"/> is load-bearing beyond authorization.</b> The backend's
/// <c>RecipientResolver</c> also uses it as an audience selector — "finance staff = users whose role
/// carries the <c>finance.read</c> permission claim". That usage is <b>not</b> ported: nothing in
/// <c>Identity.Contracts</c> enumerates users by permission, and F1-08's <c>InvoiceDueJob</c> resolves
/// its recipients from the project owner and client instead. See F1-00b in <c>PROGRESS.md</c>.</para>
/// </summary>
public static class FinancePermissions
{
    public const string Read = "finance.read";

    public const string ManageCostCategory = "finance.manage_cost_category";
    public const string ManageBudget = "finance.manage_budget";

    public const string RecordPayment = "finance.record_payment";
    public const string ReversePayment = "finance.reverse_payment";

    public const string ManageInvoice = "finance.manage_invoice";
    public const string CancelInvoice = "finance.cancel_invoice";

    /// <summary>The variation authoring bucket: raise, submit, reject, cancel.</summary>
    public const string ManageVariation = "finance.manage_variation";

    public const string GenerateBoq = "finance.generate_boq";

    /// <summary>The three sensitive variation transitions, each separately grantable. Nested for the
    /// same reason <c>SitesPermissions.Definitions</c> is: the strings carry a second dot, and nesting
    /// makes that visible at the use site rather than hiding it in a flat constant name.</summary>
    public static class Variations
    {
        public const string Approve = "finance.variations.approve";
        public const string Sign = "finance.variations.sign";
        public const string Apply = "finance.variations.apply";
    }

    /// <summary>Every string above, in declaration order. The one list <c>FinanceModule</c> registers
    /// as its <c>IModulePermissions</c> set — never re-typed at the registration site, and the value
    /// F1-09's smoke test adds to the running total rather than re-typing a literal.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        Read,
        ManageCostCategory, ManageBudget,
        RecordPayment, ReversePayment,
        ManageInvoice, CancelInvoice,
        ManageVariation,
        GenerateBoq,

        Variations.Approve, Variations.Sign, Variations.Apply,
    ];
}
