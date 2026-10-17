using SharedKernel;

namespace Hr.Domain;

internal sealed class Employee : Entity, IAuditable
{
    public string JobTitle { get; private set; } = string.Empty;
    public DateOnly HireDate { get; private set; }
    public DateOnly? EndDate { get; private set; }
    public string? Notes { get; private set; }
    public ContractType ContractType { get; private set; }
    public string? PhoneNumber { get; private set; }
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
            ContractType = contractType,
            PhoneNumber = phoneNumber,
            EmergencyContactPhone = emergencyContactPhone,
            Notes = notes,
        };

    public void Update(
        string? jobTitle = null,
        ContractType? contractType = null,
        string? phoneNumber = null,
        string? emergencyContactPhone = null,
        string? notes = null,
        DateOnly? endDate = null)
    {
        if (!IsActive)
        {
            return;
        }

        if (jobTitle is not null)
        {
            JobTitle = jobTitle;
        }

        if (contractType is not null)
        {
            ContractType = contractType.Value;
        }

        if (phoneNumber is not null)
        {
            PhoneNumber = phoneNumber;
        }

        if (emergencyContactPhone is not null)
        {
            EmergencyContactPhone = emergencyContactPhone;
        }

        if (notes is not null)
        {
            Notes = notes;
        }

        if (endDate is not null)
        {
            EndDate = endDate;
        }
    }

    public void Deactivate() => SetActive(false);

    public void Activate() => SetActive(true);
}
