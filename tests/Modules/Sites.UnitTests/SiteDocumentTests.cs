using SharedKernel;
using Sites.Domain;
using Xunit;

namespace Sites.UnitTests;

/// <summary>
/// <c>SiteDocument</c> entity behavior. <b>New in this port — the backend has no test for this type</b>,
/// including none for its exactly-one-subject rule, which is the only invariant it has.
/// </summary>
public class SiteDocumentTests
{
    private static readonly Guid DocumentId = Guid.NewGuid();
    private static readonly Guid AddedById = Guid.NewGuid();

    [Fact]
    public void Create_ForASite_LeavesTheRecordSubjectNull()
    {
        var siteId = Guid.NewGuid();

        SiteDocument link = SiteDocument
            .Create(DocumentId, SiteDocumentKind.Sketch, AddedById, siteId: siteId).Value!;

        Assert.Equal(siteId, link.SiteId);
        Assert.Null(link.RecordId);
        Assert.Equal(SiteDocumentKind.Sketch, link.Kind);
        Assert.Equal(DocumentId, link.DocumentId);
        Assert.Equal(AddedById, link.AddedById);
        Assert.True(link.IsActive);
    }

    [Fact]
    public void Create_ForARecord_LeavesTheSiteSubjectNull()
    {
        var recordId = Guid.NewGuid();

        SiteDocument link = SiteDocument
            .Create(DocumentId, SiteDocumentKind.Photo, AddedById, recordId: recordId).Value!;

        Assert.Equal(recordId, link.RecordId);
        Assert.Null(link.SiteId);
    }

    /// <summary>
    /// The XOR, both ways. Written as two explicit cases rather than one because the implementation is
    /// a single <c>siteId.HasValue == recordId.HasValue</c> test — an implementation that only rejected
    /// "both set" would pass a test that only covered "both set".
    /// </summary>
    [Fact]
    public void Create_WithNeitherSubject_IsRefused()
    {
        Result<SiteDocument> result = SiteDocument.Create(DocumentId, SiteDocumentKind.Video, AddedById);

        Assert.Equal(SiteErrors.InvalidDocumentSubject, result.Error);
        Assert.Equal(400, result.Error.Status);
    }

    [Fact]
    public void Create_WithBothSubjects_IsRefused()
    {
        Result<SiteDocument> result = SiteDocument.Create(
            DocumentId, SiteDocumentKind.Video, AddedById, Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(SiteErrors.InvalidDocumentSubject, result.Error);
        Assert.Equal(400, result.Error.Status);
    }

    [Fact]
    public void Create_WithoutADocumentOrAuthor_IsRefused()
    {
        Assert.Equal(
            SiteErrors.InvalidData,
            SiteDocument.Create(Guid.Empty, SiteDocumentKind.Photo, AddedById, Guid.NewGuid()).Error);
        Assert.Equal(
            SiteErrors.InvalidData,
            SiteDocument.Create(DocumentId, SiteDocumentKind.Photo, Guid.Empty, Guid.NewGuid()).Error);
    }

    /// <summary>
    /// The missing-subject check runs AFTER the document/author check, so a call that is wrong in both
    /// ways reports the ids first. Asserted so the ordering is a decision rather than an accident.
    /// </summary>
    [Fact]
    public void Create_WithBothProblems_ReportsTheMissingIdsFirst()
    {
        Result<SiteDocument> result = SiteDocument.Create(Guid.Empty, SiteDocumentKind.Photo, Guid.Empty);

        Assert.Equal(SiteErrors.InvalidData, result.Error);
    }

    [Fact]
    public void Deactivate_UnlinksWithoutTouchingTheDocumentReference()
    {
        SiteDocument link = SiteDocument
            .Create(DocumentId, SiteDocumentKind.Sketch, AddedById, siteId: Guid.NewGuid()).Value!;

        link.Deactivate();

        Assert.False(link.IsActive);
        Assert.Equal(DocumentId, link.DocumentId);

        link.Activate();
        Assert.True(link.IsActive);
    }
}
