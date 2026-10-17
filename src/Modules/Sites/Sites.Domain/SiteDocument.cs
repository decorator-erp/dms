using SharedKernel;

namespace Sites.Domain;

/// <summary>
/// Links an existing <c>documents.project_documents</c> row to the survey or record it evidences —
/// مرفقات الرفع, the sketch / video / photos group at the head of the paper form.
///
/// <para><b>Mirrors <c>CrmArtifactDocument</c>: this stores no file and introduces no storage path.</b>
/// The upload goes through the Documents module as it already does, and this table only says what the
/// resulting document is evidence <i>of</i>. Deleting one of these rows unlinks; the document and its
/// bytes keep their own lifecycle. There is deliberately no <c>IFileStorageService</c> reference and no
/// upload command anywhere in this module, so none of the file-upload hardening checklist applies.</para>
///
/// <para>One asymmetry with CRM worth knowing: <c>crm.crm_artifact_documents.DocumentId</c> carries no
/// foreign key, because crm migrates BEFORE documents and the ordering cycle forced dropping it.
/// <see cref="DocumentId"/> here does carry one — sites migrates after documents. Two structurally
/// identical links, one constrained and one not, for a reason entirely about migration order.</para>
/// </summary>
internal sealed class SiteDocument : Entity, IAuditable
{
    public Guid DocumentId { get; private set; }

    /// <summary>Exactly one of this and <see cref="RecordId"/> is set — a sketch belongs to the survey,
    /// a defect photo to one record.</summary>
    public Guid? SiteId { get; private set; }

    public Guid? RecordId { get; private set; }

    public SiteDocumentKind Kind { get; private set; }

    public Guid AddedById { get; private set; }

    private SiteDocument() { }

    public static Result<SiteDocument> Create(
        Guid documentId, SiteDocumentKind kind, Guid addedById,
        Guid? siteId = null, Guid? recordId = null)
    {
        if (documentId == Guid.Empty || addedById == Guid.Empty)
        {
            return SiteErrors.InvalidData;
        }

        // XOR: HasValue equality means both set or neither set, and both are wrong.
        if (siteId.HasValue == recordId.HasValue)
        {
            return SiteErrors.InvalidDocumentSubject;
        }

        return new SiteDocument
        {
            DocumentId = documentId,
            Kind = kind,
            AddedById = addedById,
            SiteId = siteId,
            RecordId = recordId,
        };
    }

    /// <summary>Unlink. The underlying document row is untouched — see the type summary.</summary>
    public void Deactivate() => SetActive(false);

    public void Activate() => SetActive(true);
}
