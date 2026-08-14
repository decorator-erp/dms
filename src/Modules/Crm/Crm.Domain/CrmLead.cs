using SharedKernel;

namespace Crm.Domain;

/// <summary>
/// Ported from <c>../backend/Domain/Entities/CrmLead.cs</c>. Slim lead intake; conversion creates a
/// <see cref="Client"/> (+ contacts) and links back.
///
/// <para><b>Bug fixed on port:</b> the backend's <c>Convert(Guid clientId)</c> accepted any Guid with
/// no validation at all — safe only because its one caller (<c>ConvertLeadCommandHandler</c>)
/// independently checked the client existed first. That is an invariant the domain layer left
/// unenforced: any other caller could set a dangling <see cref="ConvertedClientId"/>. This port closes
/// the cheap half of that gap here — <see cref="Convert"/> now rejects <c>Guid.Empty"</c> — and leaves
/// the expensive half (does this id name a real client row?) to <c>Crm.Application</c>'s
/// <c>ConvertCrmLeadCommandHandler</c> (G9-09), which has database access this Domain project
/// deliberately does not. Recorded as a deliberate divergence in this task's PROGRESS.md entry, not
/// silently "fixed."</para>
/// </summary>
internal sealed class CrmLead : Entity, IAuditable
{
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public string? Email { get; private set; }
    public string? Phone { get; private set; }
    public LeadSource Source { get; private set; }
    public LeadStatus Status { get; private set; }
    public Guid OwnerId { get; private set; }
    public string? Notes { get; private set; }
    public Guid? ConvertedClientId { get; private set; }
    public DateTime? ConvertedAtUtc { get; private set; }

    private CrmLead() { }

    public static Result<CrmLead> Create(
        string firstName,
        string lastName,
        LeadSource source,
        Guid ownerId,
        string? email = null,
        string? phone = null,
        string? notes = null)
    {
        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
            return CrmLeadErrors.NameRequired;

        return new CrmLead
        {
            FirstName = firstName,
            LastName = lastName,
            Source = source,
            Status = LeadStatus.New,
            OwnerId = ownerId,
            Email = email,
            Phone = phone,
            Notes = notes,
        };
    }

    public Result Update(
        string firstName, string lastName, LeadSource source, Guid ownerId,
        string? email, string? phone, string? notes)
    {
        if (Status == LeadStatus.Converted)
            return CrmLeadErrors.AlreadyConverted;

        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
            return CrmLeadErrors.NameRequired;

        FirstName = firstName;
        LastName = lastName;
        Source = source;
        OwnerId = ownerId;
        Email = email;
        Phone = phone;
        Notes = notes;
        return Result.Success();
    }

    public Result ChangeStatus(LeadStatus status)
    {
        if (Status == LeadStatus.Converted)
            return CrmLeadErrors.AlreadyConverted;

        // Conversion happens only through Convert(), which links the client back.
        if (status == LeadStatus.Converted)
            return CrmLeadErrors.InvalidStatusTransition;

        Status = status;
        return Result.Success();
    }

    public Result Convert(Guid clientId, DateTime? nowUtc = null)
    {
        if (Status == LeadStatus.Converted)
            return CrmLeadErrors.AlreadyConverted;

        if (clientId == Guid.Empty)
            return CrmLeadErrors.InvalidClientId;

        Status = LeadStatus.Converted;
        ConvertedClientId = clientId;
        ConvertedAtUtc = nowUtc ?? DateTime.UtcNow;
        return Result.Success();
    }
}
