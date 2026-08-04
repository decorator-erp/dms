using BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Projects.Domain;

namespace Projects.Infrastructure.Persistence.Configurations;

internal sealed class StageTeamMemberConfiguration : IEntityTypeConfiguration<StageTeamMember>
{
    public void Configure(EntityTypeBuilder<StageTeamMember> builder)
    {
        builder.ToTable("stage_team_members");
        builder.ConfigureEntityConventions();

        builder.Property(m => m.Role).HasConversion<int>();
        builder.Ignore(m => m.CanExecuteTasks);

        builder.HasIndex(m => m.StageTeamId);
        // EmployeeId: bare Guid FK to hr.employees, raw-SQL constraint in the migration - the hot path
        // for IProjectAccessGuard's membership check (G5-05), so it gets its own index too.
        builder.HasIndex(m => m.EmployeeId);
    }
}
