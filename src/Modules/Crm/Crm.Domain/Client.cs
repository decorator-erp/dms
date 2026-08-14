using SharedKernel;

namespace Crm.Domain;

/// <summary>
/// Ported from <c>../backend/Domain/Entities/Client.cs</c>. Two deliberate departures from the
/// backend, both structural to this port rather than bug fixes:
///
/// <list type="bullet">
/// <item><description><b>No <c>_projects</c> collection.</b> A client does not navigate to projects;
/// projects reference a client (<c>Projects.Domain.Project.ClientId</c>, validated at G9-13). Dropping
/// this is the cycle prerequisite that makes CRM ownership of <see cref="Client"/> legal — see
/// <c>PLAN-PHASE4.md</c>'s pre-resolved facts.</description></item>
/// <item><description><b><see cref="AppUserId"/> is a bare <c>string</c>, no <c>AppUser</c>
/// navigation.</b> It points at <c>identity.users</c> (an FK is added at G9-08, no EF navigation ever
/// — cross-schema references are bare columns everywhere in this codebase).</description></item>
/// </list>
/// </summary>
internal sealed class Client : Entity, IAuditable
{
    private readonly List<ClientChild> _children = [];

    public string AppUserId { get; private set; } = string.Empty;
    public string Address { get; private set; } = string.Empty;
    public string? Notes { get; private set; }
    public string? JobTitle { get; private set; }
    public string? MaritalStatus { get; private set; }
    public string? RepresentativeName { get; private set; }
    public IReadOnlyCollection<ClientChild> Children => _children.AsReadOnly();

    private Client() { }

    public static Result<Client> Create(string appUserId, string address, string? notes = null)
    {
        if (string.IsNullOrWhiteSpace(appUserId)) return ClientErrors.AppUserIdRequired;
        if (string.IsNullOrWhiteSpace(address)) return ClientErrors.AddressRequired;

        return new Client
        {
            AppUserId = appUserId,
            Address = address,
            Notes = notes,
        };
    }

    public void UpdateProfile(
        string? jobTitle = null, string? maritalStatus = null, string? representativeName = null)
    {
        if (jobTitle is not null) JobTitle = jobTitle;
        if (maritalStatus is not null) MaritalStatus = maritalStatus;
        if (representativeName is not null) RepresentativeName = representativeName;
    }

    /// <summary>Hop 1 of lead conversion — fill blanks from the converted lead's intake. Never
    /// overwrites office-entered data: <see cref="JobTitle"/> only when currently null,
    /// <see cref="Address"/> only when currently empty/whitespace.</summary>
    public void FillBlanksFromIntake(string? jobTitle, string? clientAddress)
    {
        JobTitle ??= jobTitle;
        if (string.IsNullOrWhiteSpace(Address) && !string.IsNullOrWhiteSpace(clientAddress))
            Address = clientAddress;
    }

    public void Deactivate() => SetActive(false);

    public void Activate() => SetActive(true);

    public Result<Guid> AddChild(char gender, short age)
    {
        var child = ClientChild.Create(gender, age);
        if (child.IsFailure) return child.Error;

        _children.Add(child.Value!);
        return child.Value!.Id;
    }

    public Result UpdateChild(Guid childId, char gender, short age)
    {
        var child = _children.FirstOrDefault(c => c.Id == childId);
        if (child is null) return ClientErrors.ChildNotFound;

        return child.Update(gender, age);
    }

    public Result RemoveChild(Guid childId)
    {
        var child = _children.FirstOrDefault(c => c.Id == childId);
        if (child is null) return ClientErrors.ChildNotFound;

        _children.Remove(child);
        return Result.Success();
    }
}
