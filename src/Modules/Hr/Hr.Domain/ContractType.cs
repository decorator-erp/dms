namespace Hr.Domain;

/// <summary>Ported from <c>../backend/Domain/Enums/ContractType.cs</c> — per D3, this is the one HR
/// enum actually referenced by <see cref="Employee"/>. The backend's other two lead-intake enums
/// (<c>HiringReason</c>, <c>PastExperience</c>) are referenced only from CRM features entirely out of
/// scope here, and are not ported.</summary>
internal enum ContractType
{
    FullTime = 1,
    PartTime,
    Contract,
    Internship,
}
