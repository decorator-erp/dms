using BuildingBlocks.Auditing;
using BuildingBlocks.Outbox;
using Documents.Domain;
using Microsoft.EntityFrameworkCore;

namespace Documents.Infrastructure.Persistence;

internal sealed class DocumentsDbContext(DbContextOptions<DocumentsDbContext> options) : DbContext(options)
{
    public DbSet<ProjectDocument> ProjectDocuments => Set<ProjectDocument>();
    public DbSet<DocumentFolder> DocumentFolders => Set<DocumentFolder>();
    public DbSet<FileTemplate> FileTemplates => Set<FileTemplate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("documents");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DocumentsDbContext).Assembly);
        modelBuilder.ConfigureOutbox(ownsMigration: false);
        modelBuilder.ConfigureAudit(ownsMigration: false);
    }
}
