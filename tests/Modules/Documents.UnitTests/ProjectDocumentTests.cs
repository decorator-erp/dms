using Documents.Domain;
using Xunit;

namespace Documents.UnitTests;

public class ProjectDocumentTests
{
    private static ProjectDocument CreateDocument() =>
        ProjectDocument.Create(
            Guid.NewGuid(), "drawing.pdf", "projects/abc/x.pdf", "application/pdf", 1024, Guid.NewGuid()).Value!;

    [Fact]
    public void Create_SetsRootDocumentIdToItsOwnId_AndVersionOne()
    {
        var document = CreateDocument();

        Assert.Equal(document.Id, document.RootDocumentId);
        Assert.Equal(1, document.Version);
        Assert.False(document.IsLocked);
    }

    [Fact]
    public void Create_RaisesDocumentUploadedDomainEvent()
    {
        var document = CreateDocument();

        var raised = Assert.Single(document.DomainEvents);
        Assert.IsType<DocumentUploadedDomainEvent>(raised);
    }

    [Fact]
    public void Create_EmptyFileName_Fails()
    {
        var result = ProjectDocument.Create(
            Guid.NewGuid(), "", "path", "application/pdf", 1024, Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal(ProjectDocumentErrors.FileNameRequired, result.Error);
    }

    [Fact]
    public void Create_ZeroFileSize_Fails()
    {
        var result = ProjectDocument.Create(
            Guid.NewGuid(), "a.pdf", "path", "application/pdf", 0, Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal(ProjectDocumentErrors.InvalidFileSize, result.Error);
    }

    [Fact]
    public void CreateRevision_CarriesTheSameRootDocumentId_AndIncrementsVersion()
    {
        var v1 = CreateDocument();

        var v2Result = ProjectDocument.CreateRevision(
            v1, "drawing-r1.pdf", "projects/abc/y.pdf", "application/pdf", 2048, Guid.NewGuid());

        Assert.True(v2Result.IsSuccess);
        var v2 = v2Result.Value!;
        Assert.Equal(v1.RootDocumentId, v2.RootDocumentId);
        Assert.Equal(2, v2.Version);
        Assert.NotEqual(v1.Id, v2.Id);
    }

    [Fact]
    public void CreateRevision_ChainOfThree_AllShareOneRoot_VersionsIncrementSequentially()
    {
        var v1 = CreateDocument();
        var v2 = ProjectDocument.CreateRevision(v1, "b.pdf", "p2", "application/pdf", 10, Guid.NewGuid()).Value!;
        var v3 = ProjectDocument.CreateRevision(v2, "c.pdf", "p3", "application/pdf", 10, Guid.NewGuid()).Value!;

        Assert.Equal(v1.RootDocumentId, v2.RootDocumentId);
        Assert.Equal(v1.RootDocumentId, v3.RootDocumentId);
        Assert.Equal(1, v1.Version);
        Assert.Equal(2, v2.Version);
        Assert.Equal(3, v3.Version);
    }

    [Fact]
    public void CreateRevision_OnALockedDocument_Fails()
    {
        var v1 = CreateDocument();
        v1.Lock();

        var result = ProjectDocument.CreateRevision(
            v1, "b.pdf", "p2", "application/pdf", 10, Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal(ProjectDocumentErrors.Locked, result.Error);
    }

    [Fact]
    public void Lock_Twice_SecondCallIsRejected()
    {
        var document = CreateDocument();
        document.Lock();

        var result = document.Lock();

        Assert.True(result.IsFailure);
        Assert.Equal(ProjectDocumentErrors.AlreadyLocked, result.Error);
    }

    [Fact]
    public void MoveToFolder_WhileLocked_Fails()
    {
        var document = CreateDocument();
        document.Lock();

        var result = document.MoveToFolder(Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal(ProjectDocumentErrors.Locked, result.Error);
    }

    [Fact]
    public void Deactivate_WhileLocked_Fails()
    {
        var document = CreateDocument();
        document.Lock();

        var result = document.Deactivate();

        Assert.True(result.IsFailure);
        Assert.Equal(ProjectDocumentErrors.Locked, result.Error);
    }

    [Fact]
    public void Deactivate_NotLocked_SucceedsAndKeepsFileFieldsIntact()
    {
        var document = CreateDocument();
        var storagePathBeforeDelete = document.StoragePath;

        var result = document.Deactivate();

        Assert.True(result.IsSuccess);
        Assert.False(document.IsActive);
        // Soft delete only - the blob path is never cleared, per G7-02's correction 4 (keep the blob).
        Assert.Equal(storagePathBeforeDelete, document.StoragePath);
    }
}
