namespace Projects.Contracts;

/// <summary>
/// Marker for a command/query whose access must be checked against <see cref="IProjectAccessGuard"/>
/// before the handler runs — mirrors the backend's <c>ISiteScoped</c>. <see cref="UserId"/> travels on
/// the message itself (never bound from the request body — the endpoint populates it from
/// <c>ICurrentUser</c>) because <c>Projects.Application</c> takes no ASP.NET dependency and so cannot
/// read the HTTP principal directly. See <c>ProjectScopeDecorator</c> (G5-07) for how this gets enforced:
/// a Projects-module-owned decorator, not a change to the shared <c>ModuleCqrsRegistration</c>.
/// </summary>
public interface IProjectScoped
{
    Guid ProjectId { get; }
    string UserId { get; }
}
