using BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Projects.Domain;

namespace Projects.Infrastructure.Persistence.Configurations;

internal sealed class StageTeamConfiguration : IEntityTypeConfiguration<StageTeam>
{
    public void Configure(EntityTypeBuilder<StageTeam> builder)
    {
        builder.ToTable("stage_teams");
        builder.ConfigureEntityConventions();

        builder.HasIndex(t => t.ProjectStageId);
        // HeadId/DepartmentId: bare Guid FKs to hr.employees/hr.departments, raw-SQL constraints in the
        // migration. R1-12: HeadId is indexed — every access check reads it (a team head is Manager on the
        // project) and its foreign key's checks would otherwise scan the table.
        builder.HasIndex(t => t.HeadId);

        // Members lives on StageTeamMember's own table (its own DbSet, its own Id) - a regular
        // relationship via the real StageTeamMember.StageTeamId property, not an EF-owned collection
        // (unlike Approvals' ApproverEntry, StageTeamMember needs independent querying - G5-05's access
        // guard queries StageTeamMembers directly).
        builder.HasMany(t => t.Members)
            .WithOne()
            .HasForeignKey(m => m.StageTeamId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
