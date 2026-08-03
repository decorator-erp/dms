using SharedKernel;

namespace Hr.Domain;

/// <summary>
/// Ported from <c>../backend/Domain/Entities/Employee.cs</c>. <see cref="AppUserId"/> is a bare
/// <c>string</c> id — NOT a navigation to <c>ApplicationUser</c> (the backend's <c>Employee.AppUser</c>
/// property doesn't exist here). That is the module boundary, and it is the single most important line
/// in this module (G2-02).
///
/// <see cref="IsActive"/>/<see cref="Deactivate"/>/<see cref="Activate"/> come from
/// <see cref="Entity.SetActive"/> rather than a private field of their own, unlike
/// <c>Identity.Domain.ApplicationUser</c> (which derives from <c>IdentityUser&lt;string&gt;</c> and so
/// has its own <c>IsActive</c>) — <see cref="Entity"/> already provides one.
/// </summary>
internal sealed class Employee : Entity, IAuditable
{
    public string JobTitle { get; private set; } = string.Empty;
    public DateOnly HireDate { get; private set; }
    public DateOnly? EndDate { get; private set; }
    public string? Notes { get; private set; }
    public ContractType ContractType { get; private set; }
    public string? PhoneNumber { get; private set; }
    public string? EmergencyContactName { get; private set; }
    public string? EmergencyContactPhone { get; private set; }
    public string AppUserId { get; private set; } = string.Empty;

    private Employee()
    {
        // EF materialization.
    }

    public static Employee Create(
        string appUserId,
        string jobTitle,
        DateOnly hireDate,
        ContractType contractType,
        string? phoneNumber = null,
        string? emergencyContactName = null,
        string? emergencyContactPhone = null,
        string? notes = null) =>
        new()
        {
            AppUserId = appUserId,
            JobTitle = jobTitle,
            HireDate = hireDate,
            ContractType = contractType,
            PhoneNumber = phoneNumber,
            EmergencyContactName = emergencyContactName,
            EmergencyContactPhone = emergencyContactPhone,
            Notes = notes,
        };

    /// <summary>
    /// No-ops on an inactive employee (ported guard from the backend) — reactivate first via
    /// <see cref="Activate"/>. Does NOT touch the app user's first/last name the way the backend's
    /// <c>EmployeeDomainService.UpdateAsync</c> did (it wrote directly into <c>ApplicationUser</c>
    /// through a second repository) — that was a cross-module write into <c>identity.</c> tables, which
    /// this architecture forbids outright regardless of module boundaries being "just" a repository
    /// call away. A name change belongs to a command Identity itself would need to expose; none exists
    /// yet, so this narrows scope rather than inventing one, and is recorded as a thinner-than-original
    /// feature at G2-04.
    /// </summary>
    public void Update(
        string? jobTitle = null,
        ContractType? contractType = null,
        string? phoneNumber = null,
        string? emergencyContactName = null,
        string? emergencyContactPhone = null,
        string? notes = null,
        DateOnly? endDate = null)
    {
        if (!IsActive)
            return;

        if (jobTitle is not null)
            JobTitle = jobTitle;
        if (contractType is not null)
            ContractType = contractType.Value;
        if (phoneNumber is not null)
            PhoneNumber = phoneNumber;
        if (emergencyContactName is not null)
            EmergencyContactName = emergencyContactName;
        if (emergencyContactPhone is not null)
            EmergencyContactPhone = emergencyContactPhone;
        if (notes is not null)
            Notes = notes;
        if (endDate is not null)
            EndDate = endDate;
    }

    public void Deactivate() => SetActive(false);

    public void Activate() => SetActive(true);
}
