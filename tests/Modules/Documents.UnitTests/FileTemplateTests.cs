using Documents.Domain;
using SharedKernel;
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
        FileTemplate template = CreateTemplate();

        Assert.True(template.IsActive);
    }

    [Fact]
    public void Create_NameAlreadyExists_Fails()
    {
        Result<FileTemplate> result = FileTemplate.Create(
            "Dup", FileTemplateCategory.Cad, "a.dwg", "path", "application/octet-stream", 10,
            nameAlreadyExists: true);

        Assert.True(result.IsFailure);
        Assert.Equal(FileTemplateErrors.NameAlreadyExists, result.Error);
    }

    [Fact]
    public void Create_ZeroFileSize_Fails()
    {
        Result<FileTemplate> result = FileTemplate.Create(
            "X", FileTemplateCategory.Other, "a.txt", "path", "text/plain", 0);

        Assert.True(result.IsFailure);
        Assert.Equal(FileTemplateErrors.InvalidFileSize, result.Error);
    }

    [Fact]
    public void Update_CannotChangeStoredFile()
    {
        FileTemplate template = CreateTemplate();
        string originalStoragePath = template.StoragePath;
        string originalFileName = template.FileName;

        Result result = template.Update("Renamed Cover Sheet", FileTemplateCategory.Image, "new description");

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
        FileTemplate template = CreateTemplate();

        Result result = template.Update("Someone Else's Name", FileTemplateCategory.Document, null, nameAlreadyExists: true);

        Assert.True(result.IsFailure);
        Assert.Equal(FileTemplateErrors.NameAlreadyExists, result.Error);
    }

    [Fact]
    public void Update_KeepingItsOwnName_NeverCollidesWithItself()
    {
        FileTemplate template = CreateTemplate();

        // nameAlreadyExists=true here simulates a naive "any row with this name" check finding the
        // template's OWN row - Update must not reject on the unchanged name.
        Result result = template.Update(template.Name, FileTemplateCategory.Cad, null, nameAlreadyExists: true);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ActivateDeactivate_ToggleIsActive()
    {
        FileTemplate template = CreateTemplate();

        template.Deactivate();
        Assert.False(template.IsActive);

        template.Activate();
        Assert.True(template.IsActive);
    }
}
