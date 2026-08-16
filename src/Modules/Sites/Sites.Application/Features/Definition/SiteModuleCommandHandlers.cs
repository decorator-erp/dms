using BuildingBlocks.Messaging;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using Sites.Contracts;
using Sites.Contracts.Definition;
using Sites.Domain;
using Sites.Domain.Definition;
using Sites.Infrastructure.Persistence;

namespace Sites.Application.Features.Definition;

// Every write in this file ends via SiteDefinitionWrites.SaveAsync -- save, then invalidate the schema
// cache, with concurrency conflicts translated to a 409 (defect B6).

internal sealed class CreateSiteModuleCommandValidator : AbstractValidator<CreateSiteModuleCommand>
{
    public CreateSiteModuleCommandValidator()
    {
        RuleFor(x => x.Request.Code).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Request.NameEn).NotEmpty().MaximumLength(128);
        RuleFor(x => x.Request.NameAr).NotEmpty().MaximumLength(128);
        RuleFor(x => x.Request.DescriptionEn).MaximumLength(512);
        RuleFor(x => x.Request.DescriptionAr).MaximumLength(512);
        RuleFor(x => x.Request.SortOrder).GreaterThanOrEqualTo(0);
    }
}

internal sealed class CreateSiteModuleCommandHandler(SitesDbContext db, ISiteSchemaCache schemaCache)
    : ICommandHandler<CreateSiteModuleCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(
        CreateSiteModuleCommand command, CancellationToken ct = default)
    {
        var normalized = command.Request.Code.Trim().ToLower();

        if (await db.SiteModules.AnyAsync(m => m.Code.ToLower() == normalized, ct))
            return SiteErrors.CodeTaken;

        var module = SiteModule.Create(
            command.Request.Code, command.Request.NameEn, command.Request.NameAr,
            command.Request.DescriptionEn, command.Request.DescriptionAr, command.Request.SortOrder);

        if (module.IsFailure) return module.Error;

        db.SiteModules.Add(module.Value!);

        var saved = await SiteDefinitionWrites.SaveAsync(db, schemaCache, ct);
        return saved.IsFailure ? saved.Error : module.Value!.Id;
    }
}

/// <summary>
/// Defect B8's fix on the update side. The backend validates create but not update for modules, items
/// and options alike — so a 129-character name is a 400 on create and a 500 from the column on update.
/// </summary>
internal sealed class UpdateSiteModuleCommandValidator : AbstractValidator<UpdateSiteModuleCommand>
{
    public UpdateSiteModuleCommandValidator()
    {
        RuleFor(x => x.ModuleId).NotEmpty();
        RuleFor(x => x.Request.NameEn).NotEmpty().MaximumLength(128);
        RuleFor(x => x.Request.NameAr).NotEmpty().MaximumLength(128);
        RuleFor(x => x.Request.DescriptionEn).MaximumLength(512);
        RuleFor(x => x.Request.DescriptionAr).MaximumLength(512);
        RuleFor(x => x.Request.SortOrder).GreaterThanOrEqualTo(0);
    }
}

internal sealed class UpdateSiteModuleCommandHandler(SitesDbContext db, ISiteSchemaCache schemaCache)
    : ICommandHandler<UpdateSiteModuleCommand>
{
    public async Task<Result> HandleAsync(UpdateSiteModuleCommand command, CancellationToken ct = default)
    {
        var module = await db.SiteModules.FirstOrDefaultAsync(m => m.Id == command.ModuleId, ct);
        if (module is null) return SiteErrors.ModuleNotFound;

        var updated = module.Update(
            command.Request.NameEn, command.Request.NameAr,
            command.Request.DescriptionEn, command.Request.DescriptionAr, command.Request.SortOrder);

        if (updated.IsFailure) return updated;

        return await SiteDefinitionWrites.SaveAsync(db, schemaCache, ct);
    }
}

internal sealed class DeleteSiteModuleCommandHandler(SitesDbContext db, ISiteSchemaCache schemaCache)
    : ICommandHandler<DeleteSiteModuleCommand>
{
    public async Task<Result> HandleAsync(DeleteSiteModuleCommand command, CancellationToken ct = default)
    {
        var module = await db.SiteModules.FirstOrDefaultAsync(m => m.Id == command.ModuleId, ct);
        if (module is null) return SiteErrors.ModuleNotFound;

        // The pure half of the rule lives on the entity; this is the half that needs a query.
        var deletable = module.EnsureDeletable();
        if (deletable.IsFailure) return deletable;

        // IgnoreQueryFilters deliberately: a soft-deleted record still holds the RESTRICT foreign key on
        // ModuleId. Filtered, the domain would allow the delete and Postgres would refuse it -- a 500
        // where the whole point of this check is a 409 the caller can act on.
        var hasRecords = await db.SiteRecords
            .IgnoreQueryFilters()
            .AnyAsync(r => r.ModuleId == command.ModuleId, ct);

        if (hasRecords) return SiteErrors.ModuleInUse;

        db.SiteModules.Remove(module);

        return await SiteDefinitionWrites.SaveAsync(db, schemaCache, ct);
    }
}

internal sealed class SetSiteModuleActiveCommandHandler(SitesDbContext db, ISiteSchemaCache schemaCache)
    : ICommandHandler<SetSiteModuleActiveCommand>
{
    public async Task<Result> HandleAsync(
        SetSiteModuleActiveCommand command, CancellationToken ct = default)
    {
        var module = await db.SiteModules.FirstOrDefaultAsync(m => m.Id == command.ModuleId, ct);
        if (module is null) return SiteErrors.ModuleNotFound;

        if (command.IsActive) module.Activate();
        else module.Deactivate();

        return await SiteDefinitionWrites.SaveAsync(db, schemaCache, ct);
    }
}
