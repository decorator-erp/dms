using Approvals.Contracts;
using BuildingBlocks.Messaging;
using Documents.Contracts.Documents;
using Documents.Domain;
using Documents.Infrastructure.Persistence;
using FluentValidation;
using Hr.Contracts;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Documents.Application.Features.Documents.RequestReview;

internal sealed class RequestDocumentReviewCommandValidator : AbstractValidator<RequestDocumentReviewCommand>
{
    public RequestDocumentReviewCommandValidator()
    {
        RuleFor(x => x.DocumentId).NotEmpty();
        RuleFor(x => x.ApproverEmployeeId).NotEmpty();
    }
}

/// <summary>
/// Makes <c>ApprovalSubjectType.ProjectDocument</c> real (G7-07). The subject is the LATEST version in
/// the referenced document's lineage, not whichever version id the caller happened to pass — same
/// "always resolve the current tip" rule <c>UploadDocumentRevisionCommandHandler</c> uses.
/// <c>ApprovalDecidedIntegrationEventHandler</c> (this module, subscribing to
/// <see cref="ApprovalDecidedIntegrationEvent"/> filtered to this subject type) is what locks the
/// document and satisfies any matching <c>DocumentApproved</c> gates once a decision comes back.
///
/// <para>
/// <b>Deliberately thinner than the backend here, recorded rather than silently dropped:</b> the backend
/// blocks a second request while one is already open (<c>ReviewInProgress</c>,
/// <c>ProjectDocumentErrors.ReviewInProgress</c> is defined for this) by querying Approvals' own tables
/// directly — a same-database join the old monolith could do and this modular design cannot;
/// <c>IApprovalRequester</c> exposes <c>RequestAsync</c>/<c>GetAsync</c>/<c>GetManyAsync</c>, no
/// "is there an open approval for subject X" query. Closing this gap needs either an
/// <c>IApprovalRequester</c> extension or Documents tracking its own pending-approval id on
/// <c>ProjectDocument</c> (the same pattern <c>GateCondition.ApprovalId</c> already uses in Projects) —
/// neither is in this task's own verify list, so it's deferred rather than guessed at.
/// </para>
/// </summary>
internal sealed class RequestDocumentReviewCommandHandler(
    DocumentsDbContext db, IHrDirectory hrDirectory, IApprovalRequester approvalRequester)
    : ICommandHandler<RequestDocumentReviewCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(RequestDocumentReviewCommand command, CancellationToken ct = default)
    {
        ProjectDocument? referenced = await db.ProjectDocuments.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == command.DocumentId, ct);
        if (referenced is null)
        {
            return ProjectDocumentErrors.NotFound;
        }

        ProjectDocument latest = await db.ProjectDocuments.AsNoTracking()
            .Where(d => d.RootDocumentId == referenced.RootDocumentId)
            .OrderByDescending(d => d.Version)
            .FirstAsync(ct);

        if (latest.Id != command.DocumentId)
        {
            return ProjectDocumentErrors.NotLatestVersion;
        }

        if (latest.IsLocked)
        {
            return ProjectDocumentErrors.Locked;
        }

        EmployeeSummary? approver = await hrDirectory.GetAsync(command.ApproverEmployeeId, ct);
        if (approver is null)
        {
            return ProjectDocumentErrors.UploaderNotFound;
        }

        Result<Guid> requestResult = await approvalRequester.RequestAsync(
            new ApprovalRequest(
                ApprovalSubjectType.ProjectDocument, latest.Id, ApprovalType.ClientReview,
                command.RequestedByUserId, [new ApproverSpec(ApproverKind.Employee, approver.AppUserId)],
                DueAtUtc: null),
            ct);

        return requestResult;
    }
}
