using Hr.Domain.Errors;
using SharedKernel;

namespace Hr.Domain;

internal sealed class Employee : Entity, IAuditable
{
    public string JobTitle { get; private set; } = string.Empty;
    public DateOnly HireDate { get; private set; }
    public DateOnly? EndDate { get; private set; }

    // Aug 2.8: personal data stays out of shared.audit_logs. The audit row still records that these
    // changed (ChangedColumns), only the values are redacted.
    [AuditIgnore]
    public string? Notes { get; private set; }
    public ContractType ContractType { get; private set; }
    [AuditIgnore]
    public string? PhoneNumber { get; private set; }
    [AuditIgnore]
    public string? EmergencyContactPhone { get; private set; }
    public string AppUserId { get; private set; } = string.Empty;

    private Employee() { }

    public static Employee Create(
        string appUserId,
        string jobTitle,
        DateOnly hireDate,
        ContractType contractType,
        string? phoneNumber = null,
        string? emergencyContactPhone = null,
        string? notes = null) =>
        new()
        {
            AppUserId = appUserId,
            JobTitle = jobTitle,
            HireDate = hireDate,
            ContractType = Defined(contractType),
            PhoneNumber = phoneNumber,
            EmergencyContactPhone = emergencyContactPhone,
            Notes = notes,
        };

    /// <summary>
    /// N7: a full replacement of the editable fields. A null clears an optional field; it does not mean
    /// "leave it alone", which is how a client could never clear a phone number before. An inactive
    /// employee is not edited: <see cref="EmployeeErrors.Inactive"/> (409), where this used to return
    /// silently and the caller got a 204 for a change that never happened. An end date before the hire
    /// date is refused (Aug 3.6).
    /// </summary>
    public Result Update(
        string jobTitle,
        ContractType contractType,
        string? phoneNumber,
        string? emergencyContactPhone,
        string? notes,
        DateOnly? endDate)
    {
        if (!IsActive)
        {
            return EmployeeErrors.Inactive;
        }

        if (endDate < HireDate)
        {
            return EmployeeErrors.EndDateBeforeHireDate;
        }

        JobTitle = jobTitle;
        ContractType = Defined(contractType);
        PhoneNumber = phoneNumber;
        EmergencyContactPhone = emergencyContactPhone;
        Notes = notes;
        EndDate = endDate;
        return Result.Success();
    }

    public void Deactivate() => SetActive(false);

    public void Activate() => SetActive(true);

    // The validators already refuse an undefined contract type; this is the last line, so a value like
    // 99 can never be stored by a path that skips them. The column's CHECK constraint is the database's.
    private static ContractType Defined(ContractType contractType) =>
        Enum.IsDefined(contractType)
            ? contractType
            : throw new ArgumentOutOfRangeException(nameof(contractType), contractType, "Undefined contract type.");
}
