using BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Projects.Domain;

namespace Projects.Infrastructure.Persistence.Configurations;

internal sealed class ProjectMemberConfiguration : IEntityTypeConfiguration<ProjectMember>
{
    /// <summary>One row per (project, employee): a racing second add trips this index and is mapped to
    /// <c>projects.members.AlreadyMember</c> (409).</summary>
    public const string ProjectEmployeeIndex = "IX_project_members_ProjectId_EmployeeId";

    public void Configure(EntityTypeBuilder<ProjectMember> builder)
    {
        builder.ToTable("project_members");
        builder.ConfigureEntityConventions();

        builder.Property(m => m.Access).HasConversion<int>();
        builder.Ignore(m => m.CanManage);
        builder.Ignore(m => m.CanExecuteTasks);

        builder.Property<uint>("xmin").HasColumnName("xmin").IsRowVersion();

        builder.HasIndex(m => new { m.ProjectId, m.EmployeeId }).IsUnique().HasDatabaseName(ProjectEmployeeIndex);
        // EmployeeId: bare Guid FK to hr.employees, raw-SQL constraint in the migration.
        builder.HasIndex(m => m.EmployeeId);
        // AddedById: bare Guid FK to hr.employees, raw-SQL constraint in the migration.
        builder.HasIndex(m => m.AddedById);
        builder.HasOne<Project>().WithMany().HasForeignKey(m => m.ProjectId).OnDelete(DeleteBehavior.Cascade);
    }
}
