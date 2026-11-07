using BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Projects.Domain;

namespace Projects.Infrastructure.Persistence.Configurations;

internal sealed class StageTeamMemberConfiguration : IEntityTypeConfiguration<StageTeamMember>
{
    public void Configure(EntityTypeBuilder<StageTeamMember> builder)
    {
        // NP4: each enum column refuses a value outside its enum (a NULL passes a CHECK).
        builder.ToTable("stage_team_members", t => t.HasCheckConstraint("CK_stage_team_members_Role", "\"Role\" BETWEEN 1 AND 4"));
        builder.ConfigureEntityConventions();

        builder.Property(m => m.Role).HasConversion<int>();
        builder.Ignore(m => m.CanExecuteTasks);

        builder.HasIndex(m => m.StageTeamId);
        // EmployeeId: bare Guid FK to hr.employees, raw-SQL constraint in the migration - the hot path
        // for IProjectAccessGuard's membership check (G5-05), so it gets its own index too.
        builder.HasIndex(m => m.EmployeeId);
    }
}
