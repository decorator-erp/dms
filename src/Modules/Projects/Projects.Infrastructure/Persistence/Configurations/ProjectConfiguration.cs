using BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Projects.Domain;

namespace Projects.Infrastructure.Persistence.Configurations;

internal sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    /// <summary>P8: the unique code index, named so <see cref="ProjectWrites"/> can map its violation to 409.</summary>
    public const string CodeIndex = "IX_projects_Code";

    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("projects");
        builder.ConfigureEntityConventions();

        builder.Property(p => p.Code).HasMaxLength(50).IsRequired();
        builder.Property(p => p.Name).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(2000);
        builder.Property(p => p.Status).HasConversion<int>();
        builder.Property(p => p.WorkflowType).HasConversion<int>();
        builder.Property(p => p.Address).HasMaxLength(500).IsRequired();
        builder.Property(p => p.Notes).HasMaxLength(2000);
        builder.Property(p => p.ProgressPercentage).HasPrecision(5, 2);

        builder.HasIndex(p => p.Code).IsUnique().HasDatabaseName(CodeIndex);

        // P9: Postgres' system xmin as the optimistic-concurrency token, as on members and the scope of work.
        // ProjectWrites maps a stale token to 409. Task/stage progress is never written to this row (it is
        // computed on read), so task transitions don't move it.
        builder.Property<uint>("xmin").HasColumnName("xmin").IsRowVersion();

        // ClientId: FK to crm.clients (G9-13, closing the Phase 2 deferral) - raw-SQL constraint in
        // the AddClientForeignKey migration (EF cannot emit it - two different models), validated in
        // application code by CreateProjectCommandHandler via Crm.Contracts.IClientDirectory before
        // Project.Create is ever called. OwnerId: bare Guid FK to hr.employees, same raw-SQL-migration
        // shape, added at G5-07. Both documented in docs/cross-schema-foreign-keys.md.
        builder.HasIndex(p => p.ClientId);
        builder.HasIndex(p => p.OwnerId);
    }
}
