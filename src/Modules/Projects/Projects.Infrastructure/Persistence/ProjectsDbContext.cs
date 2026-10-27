using BuildingBlocks.Auditing;
using BuildingBlocks.Outbox;
using Microsoft.EntityFrameworkCore;
using Projects.Domain;

namespace Projects.Infrastructure.Persistence;

internal sealed class ProjectsDbContext(DbContextOptions<ProjectsDbContext> options) : DbContext(options)
{
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectStage> ProjectStages => Set<ProjectStage>();
    public DbSet<ProjectTask> ProjectTasks => Set<ProjectTask>();
    public DbSet<ProjectSubTask> ProjectSubTasks => Set<ProjectSubTask>();
    public DbSet<TaskComment> TaskComments => Set<TaskComment>();
    public DbSet<StageTeam> StageTeams => Set<StageTeam>();
    public DbSet<StageTeamMember> StageTeamMembers => Set<StageTeamMember>();
    public DbSet<StageTemplate> StageTemplates => Set<StageTemplate>();
    public DbSet<Stage> Stages => Set<Stage>();
    public DbSet<StageTask> StageTasks => Set<StageTask>();
    public DbSet<StageSubTask> StageSubTasks => Set<StageSubTask>();
    public DbSet<StageDepartment> StageDepartments => Set<StageDepartment>();
    public DbSet<GateCondition> GateConditions => Set<GateCondition>();
    public DbSet<ScopeOfWork> ScopesOfWork => Set<ScopeOfWork>();
    public DbSet<ScopeOfWorkItem> ScopeOfWorkItems => Set<ScopeOfWorkItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("projects");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ProjectsDbContext).Assembly);
        modelBuilder.ConfigureOutbox(ownsMigration: false);
        modelBuilder.ConfigureAudit(ownsMigration: false);
    }
}
