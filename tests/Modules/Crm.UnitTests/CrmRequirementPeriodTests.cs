using Crm.Domain;
using Xunit;

namespace Crm.UnitTests;

public class CrmRequirementPeriodTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_WithFutureCloseTime_StartsOpen()
    {
        var result = CrmRequirementPeriod.Create(Guid.NewGuid(), Guid.NewGuid(), Now.AddDays(7), Guid.NewGuid(), Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(RequirementPeriodStatus.Open, result.Value!.Status);
    }

    [Fact]
    public void Create_WithPastCloseTime_Fails()
    {
        var result = CrmRequirementPeriod.Create(Guid.NewGuid(), null, Now.AddDays(-1), Guid.NewGuid(), Now);

        Assert.True(result.IsFailure);
        Assert.Equal(CrmPeriodErrors.InvalidCloseTime, result.Error);
    }

    [Fact]
    public void Extend_WhileOpen_UpdatesCloseTime()
    {
        var period = CrmRequirementPeriod.Create(Guid.NewGuid(), null, Now.AddDays(7), Guid.NewGuid(), Now).Value!;

        var result = period.Extend(Now.AddDays(14), Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(Now.AddDays(14), period.ClosesAtUtc);
    }

    [Fact]
    public void Extend_AfterClosed_Fails()
    {
        var period = CrmRequirementPeriod.Create(Guid.NewGuid(), null, Now.AddDays(7), Guid.NewGuid(), Now).Value!;
        Assert.True(period.Close(Now).IsSuccess);

        var result = period.Extend(Now.AddDays(14), Now);

        Assert.True(result.IsFailure);
        Assert.Equal(CrmPeriodErrors.NotOpen, result.Error);
    }

    [Fact]
    public void Close_WhileOpen_Succeeds()
    {
        var period = CrmRequirementPeriod.Create(Guid.NewGuid(), null, Now.AddDays(7), Guid.NewGuid(), Now).Value!;

        var result = period.Close(Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(RequirementPeriodStatus.Closed, period.Status);
        Assert.Equal(Now, period.ClosedAtUtc);
    }

    [Fact]
    public void Close_WhenAlreadyClosed_Fails()
    {
        var period = CrmRequirementPeriod.Create(Guid.NewGuid(), null, Now.AddDays(7), Guid.NewGuid(), Now).Value!;
        Assert.True(period.Close(Now).IsSuccess);

        var result = period.Close(Now);

        Assert.True(result.IsFailure);
        Assert.Equal(CrmPeriodErrors.NotOpen, result.Error);
    }

    [Fact]
    public void MarkCloseNotified_IsIdempotent()
    {
        var period = CrmRequirementPeriod.Create(Guid.NewGuid(), null, Now.AddDays(7), Guid.NewGuid(), Now).Value!;
        Assert.True(period.Close(Now).IsSuccess);

        Assert.True(period.MarkCloseNotified(Now).IsSuccess);
        var firstStamp = period.CloseNotifiedAtUtc;

        Assert.True(period.MarkCloseNotified(Now.AddHours(1)).IsSuccess);

        Assert.Equal(firstStamp, period.CloseNotifiedAtUtc);
    }

    [Fact]
    public void MarkCloseNotified_WhileStillOpen_Fails()
    {
        var period = CrmRequirementPeriod.Create(Guid.NewGuid(), null, Now.AddDays(7), Guid.NewGuid(), Now).Value!;

        var result = period.MarkCloseNotified(Now);

        Assert.True(result.IsFailure);
        Assert.Equal(CrmPeriodErrors.NotOpen, result.Error);
    }
}
