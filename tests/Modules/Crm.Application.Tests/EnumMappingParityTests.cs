using System.Globalization;
using Xunit;
using Domain = Crm.Domain;

namespace Crm.Application.Tests;

/// <summary>
/// The safety net <see cref="global::Crm.Application.CrmEnumMapping"/>'s own doc comment promises:
/// every value of every Contracts/Domain enum pair must share the same underlying int, in both
/// directions, or the numeric-cast mapping silently produces the wrong value. Names are asserted too,
/// EXCEPT for the two deliberately-renamed pairs (<c>CrmApprovalState</c>/<c>ApprovalState</c>,
/// <c>RequirementPriority</c>/<c>TaskPriority</c>) — those compare values only, since the whole point
/// of the rename was to change the name while keeping the value (see G9-01's remarks).
/// </summary>
public class EnumMappingParityTests
{
    public static IEnumerable<object[]> NameAndValueMatchPairs()
    {
        yield return [typeof(Crm.Contracts.ContactRole), typeof(Domain.ContactRole)];
        yield return [typeof(Crm.Contracts.LeadSource), typeof(Domain.LeadSource)];
        yield return [typeof(Crm.Contracts.LeadStatus), typeof(Domain.LeadStatus)];
        yield return [typeof(Crm.Contracts.MeetingStatus), typeof(Domain.MeetingStatus)];
        yield return [typeof(Crm.Contracts.MeetingSlotStatus), typeof(Domain.MeetingSlotStatus)];
        yield return [typeof(Crm.Contracts.MeetingType), typeof(Domain.MeetingType)];
        yield return [typeof(Crm.Contracts.ProjectSector), typeof(Domain.ProjectSector)];
        yield return [typeof(Crm.Contracts.HiringReason), typeof(Domain.HiringReason)];
        yield return [typeof(Crm.Contracts.PastExperience), typeof(Domain.PastExperience)];
        yield return [typeof(Crm.Contracts.TimelinePressure), typeof(Domain.TimelinePressure)];
        yield return [typeof(Crm.Contracts.UnitType), typeof(Domain.UnitType)];
        yield return [typeof(Crm.Contracts.UnitCondition), typeof(Domain.UnitCondition)];
        yield return [typeof(Crm.Contracts.UnitPosition), typeof(Domain.UnitPosition)];
        yield return [typeof(Crm.Contracts.BuildingKind), typeof(Domain.BuildingKind)];
        yield return [typeof(Crm.Contracts.ElevatorStatus), typeof(Domain.ElevatorStatus)];
        yield return [typeof(Crm.Contracts.FurnitureStatus), typeof(Domain.FurnitureStatus)];
        yield return [typeof(Crm.Contracts.FinishingPurpose), typeof(Domain.FinishingPurpose)];
        yield return [typeof(Crm.Contracts.CatalogAttributeType), typeof(Domain.CatalogAttributeType)];
        yield return [typeof(Crm.Contracts.CatalogSelectionMode), typeof(Domain.CatalogSelectionMode)];
        yield return [typeof(Crm.Contracts.RequirementCategory), typeof(Domain.RequirementCategory)];
        yield return [typeof(Crm.Contracts.RequirementPeriodStatus), typeof(Domain.RequirementPeriodStatus)];
        yield return [typeof(Crm.Contracts.RequirementSource), typeof(Domain.RequirementSource)];
        yield return [typeof(Crm.Contracts.CrmProvisionStatus), typeof(Domain.CrmProvisionStatus)];
        yield return [typeof(Crm.Contracts.PortalShareKind), typeof(Domain.PortalShareKind)];
    }

    public static IEnumerable<object[]> ValueOnlyMatchPairs()
    {
        // Renamed on the Contracts side to avoid colliding with Approvals.Contracts.ApprovalState /
        // Projects.Contracts.TaskPriority - see G9-01's remarks. Values must still match exactly.
        yield return [typeof(Crm.Contracts.CrmApprovalState), typeof(Domain.ApprovalState)];
        yield return [typeof(Crm.Contracts.RequirementPriority), typeof(Domain.TaskPriority)];
    }

    [Theory]
    [MemberData(nameof(NameAndValueMatchPairs))]
    public void ContractsAndDomainEnum_HaveIdenticalNamesAndValues(Type contractsType, Type domainType)
    {
        var contractsValues = Enum.GetValues(contractsType).Cast<object>()
            .ToDictionary(v => v.ToString()!, v => Convert.ToInt32(v, CultureInfo.InvariantCulture));
        var domainValues = Enum.GetValues(domainType).Cast<object>()
            .ToDictionary(v => v.ToString()!, v => Convert.ToInt32(v, CultureInfo.InvariantCulture));

        Assert.Equal(contractsValues.Count, domainValues.Count);
        foreach ((string? name, int value) in contractsValues)
        {
            Assert.True(domainValues.TryGetValue(name, out int domainValue),
                $"{domainType.Name} has no member named '{name}' (present in {contractsType.Name}).");
            Assert.Equal(value, domainValue);
        }
    }

    [Theory]
    [MemberData(nameof(ValueOnlyMatchPairs))]
    public void RenamedContractsAndDomainEnum_HaveIdenticalValues_ByPosition(Type contractsType, Type domainType)
    {
        var contractsValues = Enum.GetValues(contractsType).Cast<object>().Select(Convert.ToInt32).OrderBy(v => v).ToList();
        var domainValues = Enum.GetValues(domainType).Cast<object>().Select(Convert.ToInt32).OrderBy(v => v).ToList();

        Assert.Equal(domainValues, contractsValues);
    }
}
