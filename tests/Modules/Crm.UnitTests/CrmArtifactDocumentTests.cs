using Crm.Domain;
using Xunit;

namespace Crm.UnitTests;

public class CrmArtifactDocumentTests
{
    [Fact]
    public void Create_WithBriefSubject_AndEmployeeAuthor_Succeeds()
    {
        var result = CrmArtifactDocument.Create(
            Guid.NewGuid(), briefId: Guid.NewGuid(), spaceId: null, requirementItemId: null,
            meetingId: null, addedByEmployeeId: Guid.NewGuid(), addedByContactId: null);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Create_WithContactAuthor_Succeeds()
    {
        var result = CrmArtifactDocument.Create(
            Guid.NewGuid(), briefId: null, spaceId: Guid.NewGuid(), requirementItemId: null,
            meetingId: null, addedByEmployeeId: null, addedByContactId: Guid.NewGuid());

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Create_WithNoSubjectSet_Fails()
    {
        var result = CrmArtifactDocument.Create(
            Guid.NewGuid(), null, null, null, null, Guid.NewGuid(), null);

        Assert.True(result.IsFailure);
        Assert.Equal(CrmArtifactErrors.InvalidSubject, result.Error);
    }

    [Fact]
    public void Create_WithTwoSubjectsSet_Fails()
    {
        var result = CrmArtifactDocument.Create(
            Guid.NewGuid(), briefId: Guid.NewGuid(), spaceId: Guid.NewGuid(), requirementItemId: null,
            meetingId: null, addedByEmployeeId: Guid.NewGuid(), addedByContactId: null);

        Assert.True(result.IsFailure);
        Assert.Equal(CrmArtifactErrors.InvalidSubject, result.Error);
    }

    [Fact]
    public void Create_WithAllFourSubjectsSet_Fails()
    {
        var result = CrmArtifactDocument.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), null);

        Assert.True(result.IsFailure);
        Assert.Equal(CrmArtifactErrors.InvalidSubject, result.Error);
    }

    [Fact]
    public void Create_WithNeitherAuthorSet_Fails()
    {
        var result = CrmArtifactDocument.Create(
            Guid.NewGuid(), briefId: Guid.NewGuid(), spaceId: null, requirementItemId: null,
            meetingId: null, addedByEmployeeId: null, addedByContactId: null);

        Assert.True(result.IsFailure);
        Assert.Equal(CrmArtifactErrors.InvalidAuthor, result.Error);
    }

    [Fact]
    public void Create_WithBothAuthorsSet_Fails()
    {
        var result = CrmArtifactDocument.Create(
            Guid.NewGuid(), briefId: Guid.NewGuid(), spaceId: null, requirementItemId: null,
            meetingId: null, addedByEmployeeId: Guid.NewGuid(), addedByContactId: Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal(CrmArtifactErrors.InvalidAuthor, result.Error);
    }
}
