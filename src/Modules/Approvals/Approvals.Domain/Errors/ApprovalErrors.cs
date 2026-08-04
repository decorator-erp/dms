using SharedKernel;

namespace Approvals.Domain;

/// <summary>Ported from <c>../backend/Domain/Errors/ApprovalErrors.cs</c>. <c>OpenApprovalExists</c> is
/// enforced by <c>Approvals.Application</c> (a DB existence check across other rows — not something a
/// pure entity factory can see), not by <see cref="Approval"/> itself.</summary>
internal static class ApprovalErrors
{
    public static readonly Error AlreadyDecided =
        Error.Conflict("approvals.AlreadyDecided", "This approval has already been decided.");

    public static readonly Error CommentRequired =
        Error.BadRequest("approvals.CommentRequired", "A comment is required for this decision.");

    public static readonly Error NotFound = Error.NotFound("approvals.NotFound", "Approval was not found.");

    public static readonly Error OpenApprovalExists = Error.Conflict(
        "approvals.OpenApprovalExists", "An undecided approval already exists for this subject.");

    public static readonly Error NotAssignedApprover = Error.Forbidden(
        "approvals.NotAssignedApprover", "The current user is not an eligible approver for this approval.");

    public static readonly Error InvalidSubject =
        Error.BadRequest("approvals.InvalidSubject", "The approval subject is invalid.");
}
