namespace Crm.Domain;

// Mirrors Crm.Contracts' public enums — forced duplication, same reasoning as every other module's
// own Domain/Contracts split (Domain may reference SharedKernel only, so it can never see Contracts).
// Crm.Application converts between the two via CrmEnumMapping (added at G9-09), whose parity with
// these declarations is asserted value-by-value in Crm.Application.Tests.EnumMappingParityTests, same
// discipline as Projects.Application.ProjectsEnumMapping/EnumMappingParityTests.
//
// Added incrementally per Domain task, mirroring only the entities that task introduces — same
// pattern Projects.Domain's own Enums.cs followed across G5-02..G5-08.

/// <summary>Relationship of a CRM contact row to the client. Ported verbatim.</summary>
internal enum ContactRole
{
    Owner = 1,
    Spouse = 2,
    FamilyMember = 3,
    Representative = 4,
    Other = 5,
}
