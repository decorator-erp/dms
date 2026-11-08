using BuildingBlocks.Auditing;
using BuildingBlocks.Outbox;
using Crm.Domain;
using Microsoft.EntityFrameworkCore;

namespace Crm.Infrastructure.Persistence;

/// <summary>
/// 24 <see cref="DbSet{TEntity}"/> properties at G9 (30 now: crm-complete F6 adds two, P2-A the four portal tables) — one per entity with its own EF configuration.
/// <see cref="ClientChild"/> and <see cref="CatalogAttributeValueRules"/> deliberately have no
/// `DbSet` — the former is JSON-owned inside <see cref="Client"/> (see
/// <c>Configurations.ClientConfiguration</c>), the latter is a static rule class, not an entity. This
/// count is asserted against the module's own entity inventory by
/// <c>Crm.Infrastructure.Tests.CrmDbContextConfigurationTests</c> — see this task's PROGRESS.md entry
/// for why the count is asserted rather than assumed.
/// </summary>
internal sealed class CrmDbContext(DbContextOptions<CrmDbContext> options) : DbContext(options)
{
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<CrmContact> CrmContacts => Set<CrmContact>();
    public DbSet<CrmLead> CrmLeads => Set<CrmLead>();
    public DbSet<CrmLeadIntake> CrmLeadIntakes => Set<CrmLeadIntake>();
    public DbSet<CrmLeadIntakeResidential> CrmLeadIntakeResidentials => Set<CrmLeadIntakeResidential>();
    public DbSet<CrmMeeting> CrmMeetings => Set<CrmMeeting>();
    public DbSet<CrmMeetingSlot> CrmMeetingSlots => Set<CrmMeetingSlot>();
    public DbSet<CrmDesignBrief> CrmDesignBriefs => Set<CrmDesignBrief>();
    public DbSet<CrmBriefSpace> CrmBriefSpaces => Set<CrmBriefSpace>();
    public DbSet<CrmBriefHeadSnapshot> CrmBriefHeadSnapshots => Set<CrmBriefHeadSnapshot>();
    public DbSet<CrmSpaceType> CrmSpaceTypes => Set<CrmSpaceType>();
    public DbSet<CrmSpace> CrmSpaces => Set<CrmSpace>();
    public DbSet<CrmSpaceItem> CrmSpaceItems => Set<CrmSpaceItem>();
    public DbSet<CrmSpaceSelection> CrmSpaceSelections => Set<CrmSpaceSelection>();
    public DbSet<CrmSpaceSelectionOption> CrmSpaceSelectionOptions => Set<CrmSpaceSelectionOption>();
    public DbSet<CrmSpaceItemAttributeValue> CrmSpaceItemAttributeValues => Set<CrmSpaceItemAttributeValue>();
    public DbSet<CrmCatalogCategory> CrmCatalogCategories => Set<CrmCatalogCategory>();
    public DbSet<CrmCatalogItem> CrmCatalogItems => Set<CrmCatalogItem>();
    public DbSet<CrmCatalogAttribute> CrmCatalogAttributes => Set<CrmCatalogAttribute>();
    public DbSet<CrmCatalogAttributeOption> CrmCatalogAttributeOptions => Set<CrmCatalogAttributeOption>();
    public DbSet<CrmCatalogItemAttributeValue> CrmCatalogItemAttributeValues => Set<CrmCatalogItemAttributeValue>();
    public DbSet<CrmCatalogCategorySpaceType> CrmCatalogCategorySpaceTypes => Set<CrmCatalogCategorySpaceType>();
    public DbSet<CrmRequirementPeriod> CrmRequirementPeriods => Set<CrmRequirementPeriod>();
    public DbSet<CrmRequirementItem> CrmRequirementItems => Set<CrmRequirementItem>();
    public DbSet<CrmClientNote> CrmClientNotes => Set<CrmClientNote>();
    public DbSet<CrmArtifactDocument> CrmArtifactDocuments => Set<CrmArtifactDocument>();
    public DbSet<CrmPortalSettings> CrmPortalSettings => Set<CrmPortalSettings>();
    public DbSet<CrmPortalShare> CrmPortalShares => Set<CrmPortalShare>();
    public DbSet<CrmPortalUpdate> CrmPortalUpdates => Set<CrmPortalUpdate>();
    public DbSet<CrmPortalUpdateShare> CrmPortalUpdateShares => Set<CrmPortalUpdateShare>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("crm");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CrmDbContext).Assembly);
        modelBuilder.ConfigureOutbox(ownsMigration: false);
        modelBuilder.ConfigureAudit(ownsMigration: false);
    }
}
