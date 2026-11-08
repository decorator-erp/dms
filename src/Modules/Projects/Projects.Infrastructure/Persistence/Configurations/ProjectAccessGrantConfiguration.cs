using BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Projects.Domain;

namespace Projects.Infrastructure.Persistence.Configurations;

/// <summary>P1-A. <c>StageIds</c> is a <c>uuid[]</c> (see <see cref="ProjectAccessGrant"/> for why not a child
/// table). The grantee's lookups (the scope and level checks on every request) go through the
/// (EmployeeId, ProjectId) index; the project's grant list through ProjectId. "At most one current or upcoming
/// grant per (project, employee)" depends on the clock, so no index can hold it: the create handler checks it
/// under the project's write lock.</summary>
internal sealed class ProjectAccessGrantConfiguration : IEntityTypeConfiguration<ProjectAccessGrant>
{
    public void Configure(EntityTypeBuilder<ProjectAccessGrant> builder)
    {
        // NP4: each enum column refuses a value outside its enum.
        builder.ToTable("project_access_grants", t =>
        {
            t.HasCheckConstraint("CK_project_access_grants_Level", "\"Level\" BETWEEN 1 AND 3");
            t.HasCheckConstraint("CK_project_access_grants_Areas", "\"Areas\" BETWEEN 1 AND 63");
            t.HasCheckConstraint(
                "CK_project_access_grants_Window", "\"ExpiresAtUtc\" IS NULL OR \"ExpiresAtUtc\" > \"StartsAtUtc\"");
        });
        builder.ConfigureEntityConventions();

        builder.Property(g => g.Level).HasConversion<int>();
        builder.Property(g => g.Areas).HasConversion<int>();
        builder.Property(g => g.StageIds)
            .HasField("_stageIds")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnType("uuid[]")
            .IsRequired();
        builder.Property(g => g.Reason).HasMaxLength(ProjectAccessGrant.ReasonMaxLength).IsRequired();
        builder.Property(g => g.RevokeReason).HasMaxLength(ProjectAccessGrant.ReasonMaxLength);

        builder.Property<uint>("xmin").HasColumnName("xmin").IsRowVersion();

        builder.HasIndex(g => new { g.EmployeeId, g.ProjectId });
        builder.HasIndex(g => g.ProjectId);
        // GrantedById / RevokedById: bare Guid FKs to hr.employees, raw-SQL constraints in the migration.
        builder.HasIndex(g => g.GrantedById);
        builder.HasIndex(g => g.RevokedById);
        builder.HasOne<Project>().WithMany().HasForeignKey(g => g.ProjectId).OnDelete(DeleteBehavior.Cascade);
    }
}
