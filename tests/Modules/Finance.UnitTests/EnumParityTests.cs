using Xunit;
using Contracts = Finance.Contracts;
using Domain = Finance.Domain;

namespace Finance.UnitTests;

/// <summary>
/// Finance declares each enum twice — <c>internal</c> in <c>Finance.Domain</c> and <c>public</c> in
/// <c>Finance.Contracts</c> — because rule 4 requires only <c>*.Contracts</c> types be public and the
/// Domain must not reference Contracts. That is the same arrangement Projects uses.
///
/// <para><b>The duplication is only safe while the numeric values agree.</b> Both are persisted and
/// projected as <c>int</c>: the Domain value is what EF writes to the column, and the Contracts value
/// is what a response DTO carries. If someone inserts a member into the middle of one copy, every
/// stored row silently remaps — an <c>ActualCost</c> reading back as a <c>ClientPayment</c>, with no
/// compiler error and no failing test anywhere else in the suite. These tests are the only thing
/// standing between that edit and production.</para>
///
/// <para>Each test compares the full member set by name <b>and</b> value in both directions, so
/// neither an added member, a removed member, nor a renumbering can pass. Comparing counts alone would
/// miss a swap; comparing names alone would miss a renumbering.</para>
/// </summary>
public class EnumParityTests
{
    [Fact]
    public void LedgerDirection_MatchesBetweenDomainAndContracts() =>
        AssertParity<Domain.LedgerDirection, Contracts.LedgerDirection>();

    [Fact]
    public void LedgerEntryType_MatchesBetweenDomainAndContracts() =>
        AssertParity<Domain.LedgerEntryType, Contracts.LedgerEntryType>();

    [Fact]
    public void PaymentMethod_MatchesBetweenDomainAndContracts() =>
        AssertParity<Domain.PaymentMethod, Contracts.PaymentMethod>();

    private static void AssertParity<TDomain, TContracts>()
        where TDomain : struct, Enum
        where TContracts : struct, Enum
    {
        var domain = Enum.GetValues<TDomain>()
            .ToDictionary(v => v.ToString(), v => Convert.ToInt32(v, System.Globalization.CultureInfo.InvariantCulture));
        var contracts = Enum.GetValues<TContracts>()
            .ToDictionary(v => v.ToString(), v => Convert.ToInt32(v, System.Globalization.CultureInfo.InvariantCulture));

        // Liveness: an enum with no members would make every assertion below trivially true.
        Assert.NotEmpty(domain);

        Assert.Equal(domain.Keys.OrderBy(k => k, StringComparer.Ordinal), contracts.Keys.OrderBy(k => k, StringComparer.Ordinal));

        foreach (var (name, value) in domain)
        {
            Assert.True(
                contracts[name] == value,
                $"{typeof(TDomain).Name}.{name} is {value} in Finance.Domain but {contracts[name]} in "
                + "Finance.Contracts. These are persisted and projected as int; a divergence silently "
                + "remaps every stored row.");
        }
    }
}
