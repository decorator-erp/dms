namespace Crm.Contracts;

/// <summary>
/// Marker for a command/query whose access must be checked against <see cref="ICrmAccessGuard"/>
/// before the handler runs — mirrors <c>Projects.Contracts.IProjectScoped</c>. <see cref="AppUserId"/>
/// travels on the message itself (never bound from the request body) because <c>Crm.Application</c>
/// takes no ASP.NET dependency and cannot read the HTTP principal directly. See
/// <c>CrmScopeDecorator</c> (G9-08) for how this gets enforced.
/// </summary>
public interface ICrmScoped
{
    string AppUserId { get; }
}
