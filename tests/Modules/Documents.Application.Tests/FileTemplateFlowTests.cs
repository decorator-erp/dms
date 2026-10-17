using System.Text;
using BuildingBlocks.Messaging;
using Documents.Contracts.FileTemplates;
using Documents.Domain;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;
using Xunit;

namespace Documents.Application.Tests;

/// <summary>G7-06 verify: file template CRUD.</summary>
[Collection(DocumentsApplicationCollection.Name)]
public sealed class FileTemplateFlowTests(DocumentsApplicationFixture fixture)
{
    private static Stream ContentStream(string text) => new MemoryStream(Encoding.UTF8.GetBytes(text));

    [Fact]
    public async Task CreateGetUpdateActivateDeactivateDelete_FileTemplate_RoundTrips()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        string name = $"Cover Sheet {Guid.NewGuid():N}";

        ICommandHandler<CreateFileTemplateCommand, Guid> create = sp.GetRequiredService<ICommandHandler<CreateFileTemplateCommand, Guid>>();
        Result<Guid> createResult = await create.HandleAsync(new CreateFileTemplateCommand(
            name, nameof(FileTemplateCategory.Document), ContentStream("template content"),
            "cover.docx", "application/vnd.openxmlformats", 17));
        Assert.True(createResult.IsSuccess);
        Guid templateId = createResult.Value;

        IQueryHandler<GetFileTemplateByIdQuery, FileTemplateResponse> getById = sp.GetRequiredService<IQueryHandler<GetFileTemplateByIdQuery, FileTemplateResponse>>();
        Result<FileTemplateResponse> getResult = await getById.HandleAsync(new GetFileTemplateByIdQuery(templateId));
        Assert.True(getResult.IsSuccess);
        Assert.Equal(name, getResult.Value!.Name);
        Assert.True(getResult.Value.IsActive);

        IQueryHandler<GetFileTemplatesQuery, IReadOnlyList<FileTemplateResponse>> getAll = sp.GetRequiredService<IQueryHandler<GetFileTemplatesQuery, IReadOnlyList<FileTemplateResponse>>>();
        Result<IReadOnlyList<FileTemplateResponse>> allResult = await getAll.HandleAsync(new GetFileTemplatesQuery());
        Assert.Contains(allResult.Value!, t => t.Id == templateId);

        Result<IReadOnlyList<FileTemplateResponse>> categoryFilteredResult = await getAll.HandleAsync(
            new GetFileTemplatesQuery(nameof(FileTemplateCategory.Document)));
        Assert.Contains(categoryFilteredResult.Value!, t => t.Id == templateId);

        ICommandHandler<UpdateFileTemplateCommand> update = sp.GetRequiredService<ICommandHandler<UpdateFileTemplateCommand>>();
        Result updateResult = await update.HandleAsync(new UpdateFileTemplateCommand(
            templateId, name, nameof(FileTemplateCategory.Cad), "updated description"));
        Assert.True(updateResult.IsSuccess);

        Result<FileTemplateResponse> afterUpdate = await getById.HandleAsync(new GetFileTemplateByIdQuery(templateId));
        Assert.Equal(nameof(FileTemplateCategory.Cad), afterUpdate.Value!.Category);
        // Update never touches the stored file - same FileName/ContentType as creation.
        Assert.Equal("cover.docx", afterUpdate.Value.FileName);

        ICommandHandler<DeactivateFileTemplateCommand> deactivate = sp.GetRequiredService<ICommandHandler<DeactivateFileTemplateCommand>>();
        Assert.True((await deactivate.HandleAsync(new DeactivateFileTemplateCommand(templateId))).IsSuccess);
        Assert.False((await getById.HandleAsync(new GetFileTemplateByIdQuery(templateId))).Value!.IsActive);

        ICommandHandler<ActivateFileTemplateCommand> activate = sp.GetRequiredService<ICommandHandler<ActivateFileTemplateCommand>>();
        Assert.True((await activate.HandleAsync(new ActivateFileTemplateCommand(templateId))).IsSuccess);
        Assert.True((await getById.HandleAsync(new GetFileTemplateByIdQuery(templateId))).Value!.IsActive);

        ICommandHandler<DeleteFileTemplateCommand> delete = sp.GetRequiredService<ICommandHandler<DeleteFileTemplateCommand>>();
        Assert.True((await delete.HandleAsync(new DeleteFileTemplateCommand(templateId))).IsSuccess);
        Assert.False((await getById.HandleAsync(new GetFileTemplateByIdQuery(templateId))).Value!.IsActive);
    }

    [Fact]
    public async Task Create_WithADuplicateName_Fails()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        string name = $"Dup {Guid.NewGuid():N}";
        ICommandHandler<CreateFileTemplateCommand, Guid> create = sp.GetRequiredService<ICommandHandler<CreateFileTemplateCommand, Guid>>();

        Result<Guid> first = await create.HandleAsync(new CreateFileTemplateCommand(
            name, nameof(FileTemplateCategory.Other), ContentStream("a"), "a.txt", "text/plain", 1));
        Assert.True(first.IsSuccess);

        Result<Guid> second = await create.HandleAsync(new CreateFileTemplateCommand(
            name, nameof(FileTemplateCategory.Other), ContentStream("b"), "b.txt", "text/plain", 1));
        Assert.True(second.IsFailure);
        Assert.Equal("file_templates.NameAlreadyExists", second.Error.Code);
    }
}
