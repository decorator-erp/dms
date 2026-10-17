using Documents.Domain;
using SharedKernel;
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
        ProjectDocument document = CreateDocument();

        Assert.Equal(document.Id, document.RootDocumentId);
        Assert.Equal(1, document.Version);
        Assert.False(document.IsLocked);
    }

    [Fact]
    public void Create_RaisesDocumentUploadedDomainEvent()
    {
        ProjectDocument document = CreateDocument();

        IDomainEvent raised = Assert.Single(document.DomainEvents);
        Assert.IsType<DocumentUploadedDomainEvent>(raised);
    }

    [Fact]
    public void Create_EmptyFileName_Fails()
    {
        Result<ProjectDocument> result = ProjectDocument.Create(
            Guid.NewGuid(), "", "path", "application/pdf", 1024, Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal(ProjectDocumentErrors.FileNameRequired, result.Error);
    }

    [Fact]
    public void Create_ZeroFileSize_Fails()
    {
        Result<ProjectDocument> result = ProjectDocument.Create(
            Guid.NewGuid(), "a.pdf", "path", "application/pdf", 0, Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal(ProjectDocumentErrors.InvalidFileSize, result.Error);
    }

    [Fact]
    public void CreateRevision_CarriesTheSameRootDocumentId_AndIncrementsVersion()
    {
        ProjectDocument v1 = CreateDocument();

        Result<ProjectDocument> v2Result = ProjectDocument.CreateRevision(
            v1, "drawing-r1.pdf", "projects/abc/y.pdf", "application/pdf", 2048, Guid.NewGuid());

        Assert.True(v2Result.IsSuccess);
        ProjectDocument v2 = v2Result.Value!;
        Assert.Equal(v1.RootDocumentId, v2.RootDocumentId);
        Assert.Equal(2, v2.Version);
        Assert.NotEqual(v1.Id, v2.Id);
    }

    [Fact]
    public void CreateRevision_ChainOfThree_AllShareOneRoot_VersionsIncrementSequentially()
    {
        ProjectDocument v1 = CreateDocument();
        ProjectDocument v2 = ProjectDocument.CreateRevision(v1, "b.pdf", "p2", "application/pdf", 10, Guid.NewGuid()).Value!;
        ProjectDocument v3 = ProjectDocument.CreateRevision(v2, "c.pdf", "p3", "application/pdf", 10, Guid.NewGuid()).Value!;

        Assert.Equal(v1.RootDocumentId, v2.RootDocumentId);
        Assert.Equal(v1.RootDocumentId, v3.RootDocumentId);
        Assert.Equal(1, v1.Version);
        Assert.Equal(2, v2.Version);
        Assert.Equal(3, v3.Version);
    }

    [Fact]
    public void CreateRevision_OnALockedDocument_Fails()
    {
        ProjectDocument v1 = CreateDocument();
        v1.Lock();

        Result<ProjectDocument> result = ProjectDocument.CreateRevision(
            v1, "b.pdf", "p2", "application/pdf", 10, Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal(ProjectDocumentErrors.Locked, result.Error);
    }

    [Fact]
    public void Lock_Twice_SecondCallIsRejected()
    {
        ProjectDocument document = CreateDocument();
        document.Lock();

        Result result = document.Lock();

        Assert.True(result.IsFailure);
        Assert.Equal(ProjectDocumentErrors.AlreadyLocked, result.Error);
    }

    [Fact]
    public void MoveToFolder_WhileLocked_Fails()
    {
        ProjectDocument document = CreateDocument();
        document.Lock();

        Result result = document.MoveToFolder(Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal(ProjectDocumentErrors.Locked, result.Error);
    }

    [Fact]
    public void Deactivate_WhileLocked_Fails()
    {
        ProjectDocument document = CreateDocument();
        document.Lock();

        Result result = document.Deactivate();

        Assert.True(result.IsFailure);
        Assert.Equal(ProjectDocumentErrors.Locked, result.Error);
    }

    [Fact]
    public void Deactivate_NotLocked_SucceedsAndKeepsFileFieldsIntact()
    {
        ProjectDocument document = CreateDocument();
        string storagePathBeforeDelete = document.StoragePath;

        Result result = document.Deactivate();

        Assert.True(result.IsSuccess);
        Assert.False(document.IsActive);
        // Soft delete only - the blob path is never cleared, per G7-02's correction 4 (keep the blob).
        Assert.Equal(storagePathBeforeDelete, document.StoragePath);
    }
}
