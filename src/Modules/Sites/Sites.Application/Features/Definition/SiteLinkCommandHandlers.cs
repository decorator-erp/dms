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

internal sealed class AttachSiteItemCommandValidator : AbstractValidator<AttachSiteItemCommand>
{
    public AttachSiteItemCommandValidator()
    {
        RuleFor(x => x.ModuleId).NotEmpty();
        RuleFor(x => x.Request.ItemId).NotEmpty();
        RuleFor(x => x.Request.SortOrder).GreaterThanOrEqualTo(0);
    }
}

internal sealed class AttachSiteItemCommandHandler(SitesDbContext db, ISiteSchemaCache schemaCache)
    : ICommandHandler<AttachSiteItemCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(
        AttachSiteItemCommand command, CancellationToken ct = default)
    {
        if (!await db.SiteModules.AnyAsync(m => m.Id == command.ModuleId, ct))
        {
            return SiteErrors.ModuleNotFound;
        }

        // Defect B16's fix: the backend checks only that the item EXISTS. site_items has no query
        // filter, so a deactivated item attaches happily, returns 201, and then never appears in the
        // schema or the pivot -- both of which require item.IsActive. The admin sees a successful attach
        // and an empty form field, with nothing anywhere saying why.
        SiteItem? item = await db.SiteItems.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == command.Request.ItemId, ct);

        if (item is null || !item.IsActive)
        {
            return SiteErrors.ItemNotFound;
        }

        SiteModuleItem? existing = await db.SiteModuleItems
            .FirstOrDefaultAsync(l => l.ModuleId == command.ModuleId && l.ItemId == command.Request.ItemId, ct);

        if (existing is not null)
        {
            if (existing.IsActive)
            {
                return SiteErrors.AlreadyLinked;
            }

            // Reattachment, not a second row. The unique index on (ModuleId, ItemId) spans inactive
            // links deliberately, and this is why: reactivating the existing link makes the values
            // captured under it visible again, where inserting a fresh link would orphan them. Without
            // this branch detach is a one-way door.
            existing.Activate();

            Result reapplied = existing.Update(
                command.Request.IsRequired, command.Request.IsVisible, command.Request.SortOrder);

            if (reapplied.IsFailure)
            {
                return reapplied.Error;
            }

            Result resaved = await SiteDefinitionWrites.SaveAsync(db, schemaCache, ct);
            return resaved.IsFailure ? resaved.Error : existing.Id;
        }

        Result<SiteModuleItem> link = SiteModuleItem.Create(
            command.ModuleId, command.Request.ItemId, command.Request.IsRequired,
            command.Request.IsVisible, command.Request.SortOrder);

        if (link.IsFailure)
        {
            return link.Error;
        }

        db.SiteModuleItems.Add(link.Value!);

        Result saved = await SiteDefinitionWrites.SaveAsync(db, schemaCache, ct);
        return saved.IsFailure ? saved.Error : link.Value!.Id;
    }
}

/// <summary>Defect B8's fix — the backend has no validator for this command.</summary>
internal sealed class UpdateSiteLinkCommandValidator : AbstractValidator<UpdateSiteLinkCommand>
{
    public UpdateSiteLinkCommandValidator()
    {
        RuleFor(x => x.ModuleId).NotEmpty();
        RuleFor(x => x.ItemId).NotEmpty();
        RuleFor(x => x.Request.SortOrder).GreaterThanOrEqualTo(0);
    }
}

internal sealed class UpdateSiteLinkCommandHandler(SitesDbContext db, ISiteSchemaCache schemaCache)
    : ICommandHandler<UpdateSiteLinkCommand>
{
    public async Task<Result> HandleAsync(UpdateSiteLinkCommand command, CancellationToken ct = default)
    {
        SiteModuleItem? link = await db.SiteModuleItems
            .FirstOrDefaultAsync(l => l.ModuleId == command.ModuleId && l.ItemId == command.ItemId, ct);

        if (link is null)
        {
            return SiteErrors.LinkNotFound;
        }

        Result updated = link.Update(
            command.Request.IsRequired, command.Request.IsVisible, command.Request.SortOrder);

        if (updated.IsFailure)
        {
            return updated;
        }

        return await SiteDefinitionWrites.SaveAsync(db, schemaCache, ct);
    }
}

internal sealed class DetachSiteItemCommandHandler(SitesDbContext db, ISiteSchemaCache schemaCache)
    : ICommandHandler<DetachSiteItemCommand>
{
    public async Task<Result> HandleAsync(DetachSiteItemCommand command, CancellationToken ct = default)
    {
        SiteModuleItem? link = await db.SiteModuleItems
            .FirstOrDefaultAsync(l => l.ModuleId == command.ModuleId && l.ItemId == command.ItemId, ct);

        if (link is null)
        {
            return SiteErrors.LinkNotFound;
        }

        // Soft: the field leaves the form, the captured values stay. Reattaching restores them.
        link.Deactivate();

        return await SiteDefinitionWrites.SaveAsync(db, schemaCache, ct);
    }
}

/// <summary>Defect B8's fix — the backend has no validator for this command.</summary>
internal sealed class ReorderSiteLinksCommandValidator : AbstractValidator<ReorderSiteLinksCommand>
{
    public ReorderSiteLinksCommandValidator()
    {
        RuleFor(x => x.ModuleId).NotEmpty();
        RuleFor(x => x.Request.Order).NotNull();
        RuleForEach(x => x.Request.Order).ChildRules(entry =>
        {
            entry.RuleFor(e => e.ItemId).NotEmpty();
            entry.RuleFor(e => e.SortOrder).GreaterThanOrEqualTo(0);
        });
    }
}

internal sealed class ReorderSiteLinksCommandHandler(SitesDbContext db, ISiteSchemaCache schemaCache)
    : ICommandHandler<ReorderSiteLinksCommand>
{
    public async Task<Result> HandleAsync(ReorderSiteLinksCommand command, CancellationToken ct = default)
    {
        if (!await db.SiteModules.AnyAsync(m => m.Id == command.ModuleId, ct))
        {
            return SiteErrors.ModuleNotFound;
        }

        // Inactive links included deliberately: reordering must be able to position a detached field so
        // that reattaching it lands where the admin left it.
        List<SiteModuleItem> links = await db.SiteModuleItems
            .Where(l => l.ModuleId == command.ModuleId)
            .ToListAsync(ct);

        // Validate-then-apply lives in the domain (SiteModuleForm.Reorder, defect B19). The backend
        // mutates as it iterates and can still fail on a later entry.
        Result reordered = SiteModuleForm.Reorder(
            links, [.. command.Request.Order.Select(o => (o.ItemId, o.SortOrder))]);

        if (reordered.IsFailure)
        {
            return reordered;
        }

        return await SiteDefinitionWrites.SaveAsync(db, schemaCache, ct);
    }
}
