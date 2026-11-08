using BuildingBlocks;
using Crm.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Crm.Infrastructure.Persistence.Configurations;

// P2-A, client portal visibility. Every ProjectId / ItemId here is a bare Guid with NO foreign key: crm migrates
// before projects, documents and sites (rule 9's allow-list has no crm -> those pairs), the same reason as
// CrmArtifactDocument.DocumentId. Items are checked through the owning module's contract when shared and re-resolved
// on every read. SharedById / AuthorId are hr.employees ids, indexed but not constrained.

internal sealed class CrmPortalSettingsConfiguration : IEntityTypeConfiguration<CrmPortalSettings>
{
    public void Configure(EntityTypeBuilder<CrmPortalSettings> builder)
    {
        builder.ToTable("crm_portal_settings");
        builder.ConfigureEntityConventions();

        // One row per project; PortalWrites maps a racing second insert to a retry-able 409.
        builder.HasIndex(s => s.ProjectId).IsUnique().HasDatabaseName(PortalWrites.SettingsIndex);
    }
}

internal sealed class CrmPortalShareConfiguration : IEntityTypeConfiguration<CrmPortalShare>
{
    public void Configure(EntityTypeBuilder<CrmPortalShare> builder)
    {
        builder.ToTable("crm_portal_shares");
        builder.ConfigureEntityConventions();

        builder.Property(s => s.ClientTitle).HasMaxLength(CrmPortalShare.TitleMaxLength);
        builder.Property(s => s.ClientNote).HasMaxLength(CrmPortalShare.NoteMaxLength);

        // At most one ACTIVE share per item; an unshared row stays for the audit trail and the item can be shared
        // again. A racing duplicate is 409 crm.portal.AlreadyShared (PortalWrites).
        builder.HasIndex(s => new { s.ProjectId, s.ItemKind, s.ItemId }).IsUnique()
            .HasFilter("\"IsActive\"").HasDatabaseName(PortalWrites.ShareIndex);
        builder.HasIndex(s => new { s.ProjectId, s.IsActive, s.SortOrder });
        builder.HasIndex(s => s.SharedById);
    }
}

internal sealed class CrmPortalUpdateConfiguration : IEntityTypeConfiguration<CrmPortalUpdate>
{
    public void Configure(EntityTypeBuilder<CrmPortalUpdate> builder)
    {
        builder.ToTable("crm_portal_updates");
        builder.ConfigureEntityConventions();

        builder.Property(u => u.Title).HasMaxLength(CrmPortalUpdate.TitleMaxLength).IsRequired();
        builder.Property(u => u.Body).HasMaxLength(CrmPortalUpdate.BodyMaxLength).IsRequired();
        builder.Ignore(u => u.IsPublished);

        builder.HasMany(u => u.Attachments).WithOne().HasForeignKey(a => a.UpdateId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(u => u.Attachments).HasField("_attachments").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(u => new { u.ProjectId, u.IsActive });
        builder.HasIndex(u => u.AuthorId);
    }
}

internal sealed class CrmPortalUpdateShareConfiguration : IEntityTypeConfiguration<CrmPortalUpdateShare>
{
    public void Configure(EntityTypeBuilder<CrmPortalUpdateShare> builder)
    {
        builder.ToTable("crm_portal_update_shares");
        builder.ConfigureEntityConventions();

        // Same schema: a real FK. Restrict, because a share is only ever soft-deleted.
        builder.HasOne<CrmPortalShare>().WithMany().HasForeignKey(a => a.ShareId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(a => new { a.UpdateId, a.ShareId }).IsUnique();
    }
}
