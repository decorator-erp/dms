using SharedKernel;

namespace Crm.Domain;

/// <summary>Ported from <c>../backend/Domain/Entities/CrmContact.cs</c>. <see cref="MarkPrimary"/>/
/// <see cref="ClearPrimary"/> stay <c>internal</c> — only <c>Crm.Application</c>'s
/// <c>SetPrimaryCrmContactCommandHandler</c> may flip which contact on a client is primary, and it
/// must demote the previous holder in the same transaction (the invariant the backend's
/// <c>CrmContactDomainService</c> enforced by owning both calls).</summary>
internal sealed class CrmContact : Entity, IAuditable
{
    public Guid ClientId { get; private set; }
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public string? Email { get; private set; }
    public string? Phone { get; private set; }
    public string? Mobile { get; private set; }
    public ContactRole Role { get; private set; }
    public bool IsPrimary { get; private set; }
    public string? Notes { get; private set; }

    private CrmContact() { }

    public static Result<CrmContact> Create(
        Guid clientId,
        string firstName,
        string lastName,
        ContactRole role,
        bool isPrimary,
        string? email = null,
        string? phone = null,
        string? mobile = null,
        string? notes = null)
    {
        var validation = ValidateNames(firstName, lastName);
        if (validation.IsFailure) return validation.Error;

        return new CrmContact
        {
            ClientId = clientId,
            FirstName = firstName,
            LastName = lastName,
            Role = role,
            IsPrimary = isPrimary,
            Email = email,
            Phone = phone,
            Mobile = mobile,
            Notes = notes,
        };
    }

    public Result Update(
        string firstName, string lastName, ContactRole role,
        string? email, string? phone, string? mobile, string? notes)
    {
        var validation = ValidateNames(firstName, lastName);
        if (validation.IsFailure) return validation;

        FirstName = firstName;
        LastName = lastName;
        Role = role;
        Email = email;
        Phone = phone;
        Mobile = mobile;
        Notes = notes;
        return Result.Success();
    }

    internal void MarkPrimary() => IsPrimary = true;

    internal void ClearPrimary() => IsPrimary = false;

    private static Result ValidateNames(string firstName, string lastName)
    {
        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
            return CrmContactErrors.NameRequired;
        return Result.Success();
    }
}
