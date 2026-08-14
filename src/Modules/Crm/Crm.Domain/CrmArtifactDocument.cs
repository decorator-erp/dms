using SharedKernel;

namespace Crm.Domain;

/// <summary>
/// Ported from <c>../backend/Domain/Entities/CrmArtifactDocument.cs</c> — links an existing
/// <c>documents.project_documents</c> row (same upload pipeline, no new storage — CRM never uploads,
/// per Phase 3 lesson 4) to the CRM artifact it evidences: exactly one of brief/space/requirement
/// item/meeting. Author is employee XOR contact: <c>project_documents.uploaded_by_id</c> stays
/// employee-only, the true author of a client-supplied artifact lives here.
///
/// <para><see cref="DocumentId"/> is a bare unvalidated Guid with <b>no FK</b> — structural item 1's
/// dropped constraint (the migration-order cycle: crm → documents would invert crm's position ahead
/// of projects in <c>SchemaOrder</c>). Validated in <c>Crm.Application</c>'s
/// <c>LinkCrmArtifactDocumentCommandHandler</c> (G9-12) against
/// <c>Documents.Contracts.IDocumentQueries.GetAsync</c> before this row is created — the entire
/// replacement for the constraint. G9-12's own verify asserts an unknown <see cref="DocumentId"/> is
/// rejected, since that check is now the only thing standing where a constraint would have been.</para>
/// </summary>
internal sealed class CrmArtifactDocument : Entity, IAuditable
{
    public Guid DocumentId { get; private set; }
    public Guid? BriefId { get; private set; }
    public Guid? SpaceId { get; private set; }
    public Guid? RequirementItemId { get; private set; }
    public Guid? MeetingId { get; private set; }
    public Guid? AddedByEmployeeId { get; private set; }
    public Guid? AddedByContactId { get; private set; }

    private CrmArtifactDocument() { }

    public static Result<CrmArtifactDocument> Create(
        Guid documentId, Guid? briefId, Guid? spaceId, Guid? requirementItemId, Guid? meetingId,
        Guid? addedByEmployeeId, Guid? addedByContactId)
    {
        var subjects = (briefId is null ? 0 : 1) + (spaceId is null ? 0 : 1)
            + (requirementItemId is null ? 0 : 1) + (meetingId is null ? 0 : 1);
        if (subjects != 1)
            return CrmArtifactErrors.InvalidSubject;

        if (addedByEmployeeId.HasValue == addedByContactId.HasValue)
            return CrmArtifactErrors.InvalidAuthor;

        return new CrmArtifactDocument
        {
            DocumentId = documentId,
            BriefId = briefId,
            SpaceId = spaceId,
            RequirementItemId = requirementItemId,
            MeetingId = meetingId,
            AddedByEmployeeId = addedByEmployeeId,
            AddedByContactId = addedByContactId,
        };
    }
}
