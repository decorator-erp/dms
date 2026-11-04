using Approvals.Contracts;
using Approvals.Infrastructure.Persistence;
using BuildingBlocks.Pagination;
using Microsoft.EntityFrameworkCore;
using Domain = Approvals.Domain;

namespace Approvals.Infrastructure.Services;

/// <summary>B1.3: see <see cref="IClientApprovalReader"/>. One count and one page query; the approver match
/// is an <c>EXISTS</c> over <c>approval_approvers</c>.</summary>
internal sealed class ClientApprovalReader(ApprovalsDbContext db) : IClientApprovalReader
{
    public async Task<PagedResult<ClientApprovalRow>> GetForClientApproverAsync(
        string appUserId, IReadOnlyCollection<ApprovalSubjectType> subjectTypes, bool decided,
        int pageNumber, int pageSize, CancellationToken ct)
    {
        List<Domain.ApprovalSubjectType> types = [.. subjectTypes.Distinct().Select(ApprovalRequester.ToDomain)];
        IQueryable<Domain.Approval> matching = db.Approvals.AsNoTracking()
            .Where(a => types.Contains(a.SubjectType)
                && a.Approvers.Any(x => x.Kind == Domain.ApproverKind.Client && x.ApproverUserId == appUserId));
        matching = decided ? matching.Where(a => a.Decision != null) : matching.Where(a => a.Decision == null);

        int total = await matching.CountAsync(ct);
        IQueryable<Domain.Approval> ordered = decided
            ? matching.OrderByDescending(a => a.DecidedAtUtc).ThenByDescending(a => a.Id)
            : matching.OrderByDescending(a => a.CreatedAtUtc).ThenByDescending(a => a.Id);
        var page = await ordered
            .Skip((pageNumber - 1) * pageSize).Take(pageSize)
            .Select(a => new
            {
                a.Id, a.SubjectType, a.SubjectId, a.CreatedAtUtc, a.DueAtUtc, a.Decision, a.DecidedAtUtc,
                a.DecidedByUserId, a.Comment,
            })
            .ToListAsync(ct);

        return new PagedResult<ClientApprovalRow>(
            [
                .. page.Select(a => new ClientApprovalRow(
                    a.Id, ApprovalRequester.ToContracts(a.SubjectType), a.SubjectId, a.CreatedAtUtc, a.DueAtUtc,
                    a.Decision is { } d ? ApprovalRequester.ToContracts(d) : null, a.DecidedAtUtc,
                    a.DecidedByUserId, a.Comment)),
            ],
            pageNumber, pageSize, total);
    }
}
