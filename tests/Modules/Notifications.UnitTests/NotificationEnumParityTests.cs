using BuildingBlocks.Auditing;
using Xunit;
using ContractsEntityType = Notifications.Contracts.NotificationEntityType;
using ContractsType = Notifications.Contracts.NotificationType;
using DomainActivityAction = Notifications.Domain.ActivityAction;
using DomainEntityType = Notifications.Domain.NotificationEntityType;
using DomainType = Notifications.Domain.NotificationType;

namespace Notifications.UnitTests;

/// <summary>
/// Domain and Contracts each declare their own copy of these enums, because the reference matrix says
/// Domain may reference SharedKernel <i>only</i> — the duplication is structural, not a choice, and the
/// same shape <c>Sites</c> and <c>Crm</c> already carry.
///
/// <para><b>Every value is persisted as an int.</b> A member added to one copy and not the other, or
/// renumbered in one copy, does not fail to compile and does not fail any other test — it silently
/// re-labels stored rows, so a notification written last week starts reading back as a different type.
/// Asserted by NAME and VALUE, in BOTH directions, so neither an addition nor a removal on either side
/// can slip through.</para>
///
/// <para><see cref="DomainActivityAction"/> is checked against <see cref="AuditAction"/> in
/// BuildingBlocks rather than against a Contracts enum: <c>AuditAction</c> has to live in BuildingBlocks
/// (<c>AuditLog</c> does, and Contracts references BuildingBlocks), and Domain may not reference that
/// either. Same rule, one layer over — and an activity row and an audit row describing the same change
/// must not disagree about what kind of change it was.</para>
/// </summary>
public class NotificationEnumParityTests
{
    [Fact]
    public void NotificationType_DomainAndContractsCopies_AreIdenticalInNameAndValue()
        => AssertParity<DomainType, ContractsType>();

    [Fact]
    public void NotificationEntityType_DomainAndContractsCopies_AreIdenticalInNameAndValue()
        => AssertParity<DomainEntityType, ContractsEntityType>();

    [Fact]
    public void ActivityAction_DomainCopy_MatchesBuildingBlocksAuditAction()
        => AssertParity<DomainActivityAction, AuditAction>();

    private static void AssertParity<TLeft, TRight>()
        where TLeft : struct, Enum
        where TRight : struct, Enum
    {
        var left = Enum.GetValues<TLeft>()
            .ToDictionary(v => v.ToString(), v => Convert.ToInt32(v))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToList();

        var right = Enum.GetValues<TRight>()
            .ToDictionary(v => v.ToString(), v => Convert.ToInt32(v))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(left, right);
    }
}
