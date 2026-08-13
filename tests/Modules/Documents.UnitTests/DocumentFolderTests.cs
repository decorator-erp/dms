using Documents.Domain;
using Xunit;

namespace Documents.UnitTests;

public class DocumentFolderTests
{
    [Fact]
    public void Create_NeitherStageNorTask_IsValid_ProjectWide()
    {
        var result = DocumentFolder.Create(Guid.NewGuid(), "Drawings");

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.ProjectStageId);
        Assert.Null(result.Value.ProjectTaskId);
    }

    [Fact]
    public void Create_StageOnly_IsValid()
    {
        var result = DocumentFolder.Create(Guid.NewGuid(), "Stage Docs", projectStageId: Guid.NewGuid());

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Create_TaskOnly_IsValid()
    {
        var result = DocumentFolder.Create(Guid.NewGuid(), "Task Docs", projectTaskId: Guid.NewGuid());

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Create_BothStageAndTask_Fails()
    {
        var result = DocumentFolder.Create(
            Guid.NewGuid(), "Both", projectStageId: Guid.NewGuid(), projectTaskId: Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal(DocumentFolderErrors.InvalidScope, result.Error);
    }

    [Fact]
    public void Create_EmptyName_Fails()
    {
        var result = DocumentFolder.Create(Guid.NewGuid(), "  ");

        Assert.True(result.IsFailure);
        Assert.Equal(DocumentFolderErrors.NameRequired, result.Error);
    }

    [Fact]
    public void Create_DefaultRequested_ButOneAlreadyExistsForScope_Fails()
    {
        var result = DocumentFolder.Create(
            Guid.NewGuid(), "Task: Foo", projectTaskId: Guid.NewGuid(), isDefault: true,
            defaultAlreadyExistsForScope: true);

        Assert.True(result.IsFailure);
        Assert.Equal(DocumentFolderErrors.DefaultAlreadyExists, result.Error);
    }

    [Fact]
    public void Create_DefaultRequested_NoneExistsYet_Succeeds()
    {
        var result = DocumentFolder.Create(
            Guid.NewGuid(), "Task: Foo", projectTaskId: Guid.NewGuid(), isDefault: true,
            defaultAlreadyExistsForScope: false);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsDefault);
    }

    [Fact]
    public void MoveTo_Self_IsRejectedAsCyclic()
    {
        var folder = DocumentFolder.Create(Guid.NewGuid(), "A").Value!;

        var result = folder.MoveTo(folder.Id, new Dictionary<Guid, Guid?>());

        Assert.True(result.IsFailure);
        Assert.Equal(DocumentFolderErrors.CyclicMove, result.Error);
    }

    [Fact]
    public void MoveTo_OwnDescendant_IsRejectedAsCyclic()
    {
        // Tree: A -> B -> C (C's parent is B, B's parent is A). Moving A under C would detach the
        // branch - exactly the case the backend has no guard for (G7-02's correction 5).
        var a = DocumentFolder.Create(Guid.NewGuid(), "A").Value!;
        var bId = Guid.NewGuid();
        var cId = Guid.NewGuid();
        var ancestorChain = new Dictionary<Guid, Guid?>
        {
            [bId] = a.Id,
            [cId] = bId,
        };

        var result = a.MoveTo(cId, ancestorChain);

        Assert.True(result.IsFailure);
        Assert.Equal(DocumentFolderErrors.CyclicMove, result.Error);
    }

    [Fact]
    public void MoveTo_UnrelatedFolder_Succeeds()
    {
        var a = DocumentFolder.Create(Guid.NewGuid(), "A").Value!;
        var otherId = Guid.NewGuid();
        var ancestorChain = new Dictionary<Guid, Guid?> { [otherId] = null };

        var result = a.MoveTo(otherId, ancestorChain);

        Assert.True(result.IsSuccess);
        Assert.Equal(otherId, a.ParentFolderId);
    }

    [Fact]
    public void MoveTo_Null_MovesToRoot_NeverRejected()
    {
        var a = DocumentFolder.Create(Guid.NewGuid(), "A", parentFolderId: Guid.NewGuid()).Value!;

        var result = a.MoveTo(null, new Dictionary<Guid, Guid?>());

        Assert.True(result.IsSuccess);
        Assert.Null(a.ParentFolderId);
    }

    [Fact]
    public void Rename_EmptyName_Fails()
    {
        var folder = DocumentFolder.Create(Guid.NewGuid(), "A").Value!;

        var result = folder.Rename("");

        Assert.True(result.IsFailure);
        Assert.Equal(DocumentFolderErrors.NameRequired, result.Error);
    }
}
