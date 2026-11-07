namespace Projects.Contracts;

/// <summary>
/// P10 (C5): the least <see cref="ProjectMemberAccess"/> the caller must hold on the project for an
/// <see cref="IProjectScoped"/> command to run. Every scoped command declares one, through
/// <see cref="IRequiresProjectEditor"/> or <see cref="IRequiresProjectManager"/> (the architecture tests
/// enforce it); a scoped query needs only scope, which is <see cref="ProjectMemberAccess.Viewer"/>.
///
/// <para><c>ProjectScopeDecorator</c> checks it after scope (404) and before the handler, so before any
/// state rule (409): a caller below the level gets 403 <c>projects.InsufficientLevel</c>. The caller's level
/// is the highest of its sources; see <see cref="IProjectAccessLevelReader"/>.</para>
/// </summary>
public interface IRequiresProjectLevel
{
    ProjectMemberAccess RequiredProjectLevel { get; }
}

/// <summary>Task execution, sub-tasks and comments.</summary>
public interface IRequiresProjectEditor : IRequiresProjectLevel
{
    ProjectMemberAccess IRequiresProjectLevel.RequiredProjectLevel => ProjectMemberAccess.Editor;
}

/// <summary>The project itself, stages, teams, gates, scope of work, members, and task planning.</summary>
public interface IRequiresProjectManager : IRequiresProjectLevel
{
    ProjectMemberAccess IRequiresProjectLevel.RequiredProjectLevel => ProjectMemberAccess.Manager;
}
