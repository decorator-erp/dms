using Approvals.Infrastructure.Persistence;
using Approvals.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;
using Xunit;

namespace Approvals.Application.Tests;

/// <summary>FINDING-P1-14: the office decide always runs inside the transaction
/// <c>DomainEventDispatchDecorator</c> opened, i.e. <see cref="ApprovalLocks.DecideLockedAsync"/>'s ambient
/// branch. A lost race there (serialization failure, deadlock, concurrency exception) must map to
/// <c>approvals.AlreadyDecided</c> (409) exactly as the owned-transaction branch does, and leave the caller's
/// transaction for the caller to roll back. A real lost race cannot be forced deterministically through the
/// row lock, so the decide step raises the error itself, inside the ambient transaction.</summary>
[Collection(ApprovalsApplicationCollection.Name)]
public sealed class AmbientDecideLostRaceTests(ApprovalsApplicationFixture fixture)
{
    [Fact]
    public async Task AmbientTransaction_SerializationFailure_IsAlreadyDecided_AndTheCallerRollsBack() =>
        await AssertAlreadyDecidedAsync(db =>
            db.Database.ExecuteSqlRawAsync("DO $$ BEGIN RAISE EXCEPTION 'lost race' USING ERRCODE = '40001'; END $$"));

    [Fact]
    public async Task AmbientTransaction_Deadlock_IsAlreadyDecided_AndTheCallerRollsBack() =>
        await AssertAlreadyDecidedAsync(db =>
            db.Database.ExecuteSqlRawAsync("DO $$ BEGIN RAISE EXCEPTION 'lost race' USING ERRCODE = '40P01'; END $$"));

    [Fact]
    public async Task AmbientTransaction_ConcurrencyException_IsAlreadyDecided_AndTheCallerRollsBack() =>
        await AssertAlreadyDecidedAsync(_ => throw new DbUpdateConcurrencyException("lost race"));

    [Fact]
    public async Task AmbientTransaction_AnUnrelatedError_StillThrows()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        ApprovalsDbContext db = scope.ServiceProvider.GetRequiredService<ApprovalsDbContext>();
        await using IDbContextTransaction ambient = await db.Database.BeginTransactionAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => ApprovalLocks.DecideLockedAsync(
            db, Guid.NewGuid(), () => throw new InvalidOperationException("not a race"), CancellationToken.None));

        await ambient.RollbackAsync();
    }

    private async Task AssertAlreadyDecidedAsync(Func<ApprovalsDbContext, Task> loseTheRace)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        ApprovalsDbContext db = scope.ServiceProvider.GetRequiredService<ApprovalsDbContext>();
        await using IDbContextTransaction ambient = await db.Database.BeginTransactionAsync();

        Result result = await ApprovalLocks.DecideLockedAsync(db, Guid.NewGuid(), async () =>
        {
            await loseTheRace(db);
            return Result.Success();
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("approvals.AlreadyDecided", result.Error.Code);
        Assert.Equal(409, result.Error.Status);

        // The caller's transaction is untouched — still the current one — and the caller can roll it back,
        // which is what DomainEventDispatchDecorator does with a failure Result.
        Assert.Same(ambient, db.Database.CurrentTransaction);
        await ambient.RollbackAsync();
    }
}
