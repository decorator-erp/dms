namespace Sites.Application;

/// <summary>
/// <c>Sites.Application</c> legitimately references both <c>Sites.Contracts</c> and
/// <c>Sites.Domain</c> — those two cannot reference each other (the reference matrix forbids
/// Domain → Contracts, and Contracts → Domain would make internals public), so the eight enum pairs
/// are duplicated by construction and every handler here converts between them.
///
/// <para>A numeric cast is correct because both copies are ported from the same backend enum, in the
/// same order, in the same commit — the same deliberate, disclosed trade-off
/// <c>CrmEnumMapping</c> and <c>ProjectsEnumMapping</c> already make. <c>SiteEnumParityTests</c> (in
/// <c>Sites.Application.Tests</c>) enumerates every value of every pair and asserts names and
/// underlying values match exactly, which is the safety an exhaustive hand-written switch would give
/// for a fraction of the code.</para>
///
/// <para>No pair is renamed across the boundary here — unlike CRM's two — because none of Sites' enum
/// names collides with another module's Contracts type.</para>
/// </summary>
internal static class SiteEnumMapping
{
    public static Domain.SiteStatus ToDomain(Contracts.SiteStatus value) => (Domain.SiteStatus)value;
    public static Contracts.SiteStatus ToContracts(Domain.SiteStatus value) => (Contracts.SiteStatus)value;

    public static Domain.SiteItemDataType ToDomain(Contracts.SiteItemDataType value) =>
        (Domain.SiteItemDataType)value;

    public static Contracts.SiteItemDataType ToContracts(Domain.SiteItemDataType value) =>
        (Contracts.SiteItemDataType)value;

    public static Domain.SiteDocumentKind ToDomain(Contracts.SiteDocumentKind value) =>
        (Domain.SiteDocumentKind)value;

    public static Contracts.SiteDocumentKind ToContracts(Domain.SiteDocumentKind value) =>
        (Contracts.SiteDocumentKind)value;

    public static Domain.UnitType? ToDomain(Contracts.UnitType? value) => (Domain.UnitType?)value;
    public static Contracts.UnitType? ToContracts(Domain.UnitType? value) => (Contracts.UnitType?)value;

    public static Domain.UnitCondition? ToDomain(Contracts.UnitCondition? value) =>
        (Domain.UnitCondition?)value;

    public static Contracts.UnitCondition? ToContracts(Domain.UnitCondition? value) =>
        (Contracts.UnitCondition?)value;

    public static Domain.BuildingKind? ToDomain(Contracts.BuildingKind? value) =>
        (Domain.BuildingKind?)value;

    public static Contracts.BuildingKind? ToContracts(Domain.BuildingKind? value) =>
        (Contracts.BuildingKind?)value;

    public static Domain.UnitPosition? ToDomain(Contracts.UnitPosition? value) =>
        (Domain.UnitPosition?)value;

    public static Contracts.UnitPosition? ToContracts(Domain.UnitPosition? value) =>
        (Contracts.UnitPosition?)value;

    public static Domain.ElevatorStatus? ToDomain(Contracts.ElevatorStatus? value) =>
        (Domain.ElevatorStatus?)value;

    public static Contracts.ElevatorStatus? ToContracts(Domain.ElevatorStatus? value) =>
        (Contracts.ElevatorStatus?)value;
}
