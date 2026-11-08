using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using Projects.Contracts.Members;
using Projects.Contracts.Projects;
using Projects.Contracts.Stages;
using Projects.Contracts.Tasks;
using Projects.Contracts.Teams;
using Projects.Infrastructure.Persistence;

namespace Projects.Infrastructure.Decorators;

/// <summary>
/// P1-A: what a project access grant must cover for an <see cref="IProjectScoped"/> message to pass
/// <see cref="ProjectScopeDecorator"/>. Membership and <c>read_all</c> ignore all of this.
/// <list type="bullet">
/// <item><b>Writes to the project itself, its scope of work, members and teams</b> (every command in the
/// <c>Projects</c>, <c>Scope</c>, <c>Members</c> and <c>Teams</c> contract namespaces): <see cref="Membership"/> — no
/// grant ever counts, at any level. Overview and Team stay read-only.</item>
/// <item><b>Stages, tasks, sub-tasks, comments and gates</b> (the <c>Stages</c>, <c>Tasks</c> and <c>Gates</c>
/// namespaces, reads and writes): <see cref="ProjectAccessArea.StagesAndTasks"/>, at the stage the message names or
/// that its task, sub-task or gate belongs to.</item>
/// <item><b>The project's own reads</b>: <see cref="GetProjectQuery"/> needs any grant (the header a grantee
/// navigates from); <see cref="GetProjectDetailsQuery"/> and the scope of work need
/// <see cref="ProjectAccessArea.Overview"/>.</item>
/// <item><b>Member and team reads</b>: <see cref="ProjectAccessArea.Team"/>.</item>
/// <item><b>Lists</b> (the details page, the stage list, the task list without a stage, the member and team lists)
/// pass on a stage-limited grant too, and their handlers keep only the granted stages' rows
/// (<see cref="IProjectAccessGuard.GetStageFilterAsync"/>).</item>
/// </list>
/// Anything not named here falls to <see cref="Membership"/>, so a message nobody classified can never be opened by
/// a grant.
/// </summary>
internal static class ProjectScopeAreas
{
    internal enum Kind
    {
        /// <summary>No grant counts (membership and read_all only).</summary>
        Membership,

        /// <summary>Any active grant counts (the guard's area-less overload).</summary>
        AnyGrant,

        /// <summary>A grant covering the area at the resolved stage (null: the project as a whole).</summary>
        Stage,

        /// <summary>A grant on the area, stage-limited or not; the handler filters rows.</summary>
        List,
    }

    internal readonly record struct Target(Kind Kind, ProjectAccessArea Area, Guid? StageId);

    private static readonly string[] MembershipOnlyWriteNamespaces =
        ["Projects.Contracts.Projects", "Projects.Contracts.Scope", "Projects.Contracts.Members", "Projects.Contracts.Teams"];

    private static readonly string[] StagesAndTasksNamespaces =
        ["Projects.Contracts.Stages", "Projects.Contracts.Tasks", "Projects.Contracts.Gates"];

    public static async Task<Target> ResolveAsync(object message, bool isWrite, ProjectsDbContext db, CancellationToken ct)
    {
        string? ns = message.GetType().Namespace;
        if (isWrite && MembershipOnlyWriteNamespaces.Contains(ns))
        {
            return new Target(Kind.Membership, ProjectAccessArea.None, null);
        }

        switch (message)
        {
            case GetProjectQuery:
                return new Target(Kind.AnyGrant, ProjectAccessArea.None, null);
            case GetProjectDetailsQuery:
                return new Target(Kind.List, ProjectAccessArea.Overview, null);
            case GetProjectMembersQuery:
                return new Target(Kind.List, ProjectAccessArea.Team, null);
            case GetProjectTeamsQuery { StageId: null }:
                return new Target(Kind.List, ProjectAccessArea.Team, null);
            case GetProjectTeamsQuery teams:
                return new Target(Kind.Stage, ProjectAccessArea.Team, teams.StageId);
            case GetStageTeamQuery team:
                return new Target(Kind.Stage, ProjectAccessArea.Team, await StageOfTeamAsync(db, team.StageTeamId, ct));
            case GetStageTeamMembersQuery team:
                return new Target(Kind.Stage, ProjectAccessArea.Team, await StageOfTeamAsync(db, team.StageTeamId, ct));
            case GetProjectStagesByProjectQuery:
            case GetProjectTasksByProjectQuery { StageId: null }:
                return new Target(Kind.List, ProjectAccessArea.StagesAndTasks, null);
        }

        if (!isWrite && ns == "Projects.Contracts.Scope")
        {
            return new Target(Kind.Stage, ProjectAccessArea.Overview, null);
        }

        if (StagesAndTasksNamespaces.Contains(ns))
        {
            return new Target(Kind.Stage, ProjectAccessArea.StagesAndTasks, await StageOfAsync(message, db, ct));
        }

        return new Target(Kind.Membership, ProjectAccessArea.None, null);
    }

    /// <summary>The stage a Stages/Tasks/Gates message is about, by the first id it carries: a stage id, then a task,
    /// sub-task or gate id resolved through this module's own rows. Null when it names none (a project-level
    /// create) or the row does not exist (the handler then answers 404).</summary>
    private static async Task<Guid?> StageOfAsync(object message, ProjectsDbContext db, CancellationToken ct)
    {
        Ids ids = IdsOf(message);
        if (ids.Stage is { } stage)
        {
            return stage;
        }

        if (ids.Task is { } task)
        {
            return await db.ProjectTasks.AsNoTracking().Where(t => t.Id == task).Select(t => t.ProjectStageId).FirstOrDefaultAsync(ct);
        }

        if (ids.SubTask is { } subTask)
        {
            return await db.ProjectSubTasks.AsNoTracking()
                .Where(s => s.Id == subTask)
                .Join(db.ProjectTasks, s => s.ProjectTaskId, t => t.Id, (_, t) => t.ProjectStageId)
                .FirstOrDefaultAsync(ct);
        }

        if (ids.Gate is { } gate)
        {
            var owner = await db.GateConditions.AsNoTracking()
                .Where(g => g.Id == gate)
                .Select(g => new { g.ProjectStageId, g.ProjectTaskId })
                .FirstOrDefaultAsync(ct);
            if (owner?.ProjectStageId is { } gateStage)
            {
                return gateStage;
            }

            return owner?.ProjectTaskId is { } gateTask
                ? await db.ProjectTasks.AsNoTracking().Where(t => t.Id == gateTask).Select(t => t.ProjectStageId).FirstOrDefaultAsync(ct)
                : null;
        }

        return null;
    }

    private static Task<Guid?> StageOfTeamAsync(ProjectsDbContext db, Guid stageTeamId, CancellationToken ct) =>
        db.StageTeams.AsNoTracking().Where(t => t.Id == stageTeamId).Select(t => (Guid?)t.ProjectStageId).FirstOrDefaultAsync(ct);

    private readonly record struct Ids(Guid? Stage, Guid? Task, Guid? SubTask, Guid? Gate);

    private static readonly ConcurrentDictionary<Type, Func<object, Ids>> Readers = new();

    /// <summary>Reads the ids by property name, once per type: <c>StageId</c>/<c>ProjectStageId</c>, <c>TaskId</c>,
    /// <c>SubTaskId</c>, <c>GateConditionId</c>.</summary>
    private static Ids IdsOf(object message) => Readers.GetOrAdd(message.GetType(), type =>
    {
        PropertyInfo? stage = type.GetProperty("StageId") ?? type.GetProperty("ProjectStageId");
        PropertyInfo? task = type.GetProperty("TaskId");
        PropertyInfo? subTask = type.GetProperty("SubTaskId");
        PropertyInfo? gate = type.GetProperty("GateConditionId");
        return m => new Ids(Read(stage, m), Read(task, m), Read(subTask, m), Read(gate, m));
    })(message);

    private static Guid? Read(PropertyInfo? property, object message) => property?.GetValue(message) switch
    {
        Guid id => id,
        _ => null,
    };
}
