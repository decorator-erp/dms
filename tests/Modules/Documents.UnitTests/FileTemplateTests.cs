using Documents.Domain;
using Xunit;

namespace Documents.UnitTests;

public class FileTemplateTests
{
    private static FileTemplate CreateTemplate() =>
        FileTemplate.Create(
            "Standard Cover Sheet", FileTemplateCategory.Document, "cover.docx", "file-templates/cover.docx",
            "application/vnd.openxmlformats", 4096).Value!;

    [Fact]
    public void Create_StartsActive()
    {
        var template = CreateTemplate();

        Assert.True(template.IsActive);
    }

    [Fact]
    public void Create_NameAlreadyExists_Fails()
    {
        var result = FileTemplate.Create(
            "Dup", FileTemplateCategory.Cad, "a.dwg", "path", "application/octet-stream", 10,
            nameAlreadyExists: true);

        Assert.True(result.IsFailure);
        Assert.Equal(FileTemplateErrors.NameAlreadyExists, result.Error);
    }

    [Fact]
    public void Create_ZeroFileSize_Fails()
    {
        var result = FileTemplate.Create(
            "X", FileTemplateCategory.Other, "a.txt", "path", "text/plain", 0);

        Assert.True(result.IsFailure);
        Assert.Equal(FileTemplateErrors.InvalidFileSize, result.Error);
    }

    [Fact]
    public void Update_CannotChangeStoredFile()
    {
        var template = CreateTemplate();
        var originalStoragePath = template.StoragePath;
        var originalFileName = template.FileName;

        var result = template.Update("Renamed Cover Sheet", FileTemplateCategory.Image, "new description");

        Assert.True(result.IsSuccess);
        Assert.Equal("Renamed Cover Sheet", template.Name);
        Assert.Equal(FileTemplateCategory.Image, template.Category);
        // Update never touches the file fields - matches the backend exactly.
        Assert.Equal(originalStoragePath, template.StoragePath);
        Assert.Equal(originalFileName, template.FileName);
    }

    [Fact]
    public void Update_ToAnAlreadyTakenDifferentName_Fails()
    {
        var template = CreateTemplate();

        var result = template.Update("Someone Else's Name", FileTemplateCategory.Document, null, nameAlreadyExists: true);

        Assert.True(result.IsFailure);
        Assert.Equal(FileTemplateErrors.NameAlreadyExists, result.Error);
    }

    [Fact]
    public void Update_KeepingItsOwnName_NeverCollidesWithItself()
    {
        var template = CreateTemplate();

        // nameAlreadyExists=true here simulates a naive "any row with this name" check finding the
        // template's OWN row - Update must not reject on the unchanged name.
        var result = template.Update(template.Name, FileTemplateCategory.Cad, null, nameAlreadyExists: true);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ActivateDeactivate_ToggleIsActive()
    {
        var template = CreateTemplate();

        template.Deactivate();
        Assert.False(template.IsActive);

        template.Activate();
        Assert.True(template.IsActive);
    }
}
