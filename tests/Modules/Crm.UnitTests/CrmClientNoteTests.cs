using Crm.Domain;
using Xunit;

namespace Crm.UnitTests;

public class CrmClientNoteTests
{
    [Fact]
    public void Create_SetsAllFields()
    {
        var clientId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var writtenById = Guid.NewGuid();

        var note = CrmClientNote.Create(clientId, projectId, "internal note", writtenById);

        Assert.Equal(clientId, note.ClientId);
        Assert.Equal(projectId, note.ProjectId);
        Assert.Equal("internal note", note.Content);
        Assert.Equal(writtenById, note.WrittenById);
    }

    [Fact]
    public void Create_WithoutProject_Succeeds()
    {
        var note = CrmClientNote.Create(Guid.NewGuid(), null, "note", Guid.NewGuid());

        Assert.Null(note.ProjectId);
    }

    [Fact]
    public void Update_ChangesContent()
    {
        var note = CrmClientNote.Create(Guid.NewGuid(), null, "original", Guid.NewGuid());

        note.Update("revised");

        Assert.Equal("revised", note.Content);
    }
}
