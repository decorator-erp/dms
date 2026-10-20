using System.Globalization;
using Xunit;
using Domain = Projects.Domain;

namespace Projects.Application.Tests;

/// <summary>
/// The safety net <see cref="global::Projects.Application.ProjectsEnumMapping"/>'s own doc comment
/// promises: every value of every Contracts/Domain enum pair must share the same name and the same
/// underlying int, in both directions, or the numeric-cast mapping silently produces the wrong value.
/// One generic assertion, run for all twelve pairs, rather than hand-written round-trip calls per pair.
/// </summary>
public class EnumMappingParityTests
{
    public static IEnumerable<object[]> EnumPairs()
    {
        yield return [typeof(Projects.Contracts.ProjectStatus), typeof(Domain.ProjectStatus)];
        yield return [typeof(Projects.Contracts.ProjectTaskStatus), typeof(Domain.ProjectTaskStatus)];
        yield return [typeof(Projects.Contracts.StageStatus), typeof(Domain.StageStatus)];
        yield return [typeof(Projects.Contracts.TaskPriority), typeof(Domain.TaskPriority)];
        yield return [typeof(Projects.Contracts.StageTeamMemberRole), typeof(Domain.StageTeamMemberRole)];
        yield return [typeof(Projects.Contracts.WorkflowType), typeof(Domain.WorkflowType)];
        yield return [typeof(Projects.Contracts.StageTemplateType), typeof(Domain.StageTemplateType)];
        yield return [typeof(Projects.Contracts.ConditionType), typeof(Domain.ConditionType)];
        yield return [typeof(Projects.Contracts.GateOwnerKind), typeof(Domain.GateOwnerKind)];
        yield return [typeof(Projects.Contracts.GateStatus), typeof(Domain.GateStatus)];
        yield return [typeof(Projects.Contracts.GateTrigger), typeof(Domain.GateTrigger)];
        yield return [typeof(Projects.Contracts.ApprovalType), typeof(Domain.ApprovalType)];
    }

    [Theory]
    [MemberData(nameof(EnumPairs))]
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
}
