using BuildingBlocks.Messaging;
using BuildingBlocks.Outbox;
using Hr.Contracts;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using Projects.Contracts.Stages;
using Projects.Infrastructure.Persistence;
using SharedKernel;
using Domain = Projects.Domain;

namespace Projects.Application.Features.Stages;

/// <summary>
/// Per <c>Projects.Domain.ProjectEvents</c>'s own remarks: the domain event
/// (<c>ProjectStageCompletedDomainEvent</c>) drives intra-module gate re-evaluation (G5-08); the
/// integration event is enqueued directly here, in the same <c>SaveChangesAsync</c> — a domain-event
/// handler staging an outbox write would land in a second, separate save.
/// </summary>
internal sealed class CompleteProjectStageCommandHandler(ProjectsDbContext db, IHrDirectory hrDirectory)
    : ICommandHandler<CompleteProjectStageCommand>
{
    public async Task<Result> HandleAsync(CompleteProjectStageCommand command, CancellationToken ct = default)
    {
        var stage = await db.ProjectStages.FirstOrDefaultAsync(
            s => s.Id == command.StageId && s.ProjectId == command.ProjectId, ct);
        if (stage is null)
            return Domain.StageErrors.NotFound;

        var employeeId = await hrDirectory.ResolveEmployeeIdAsync(command.UserId, ct);
        if (employeeId is null)
            return Domain.ProjectErrors.AccessDenied;

        var result = stage.Complete(employeeId.Value);
        if (result.IsFailure)
            return result;

        db.Outbox().Enqueue(new ProjectStageCompletedIntegrationEvent(
            Guid.NewGuid(), stage.ProjectId, stage.Id, stage.Name, DateTimeOffset.UtcNow));

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

internal sealed class RejectProjectStageCommandHandler(ProjectsDbContext db) : ICommandHandler<RejectProjectStageCommand>
{
    public async Task<Result> HandleAsync(RejectProjectStageCommand command, CancellationToken ct = default)
    {
        var stage = await db.ProjectStages.FirstOrDefaultAsync(
            s => s.Id == command.StageId && s.ProjectId == command.ProjectId, ct);
        if (stage is null)
            return Domain.StageErrors.NotFound;

        var result = stage.Reject(command.Reason);
        if (result.IsFailure)
            return result;

        db.Outbox().Enqueue(new ProjectStageRejectedIntegrationEvent(
            Guid.NewGuid(), stage.ProjectId, stage.Id, stage.Name, DateTimeOffset.UtcNow));

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

internal sealed class GetProjectStageQueryHandler(ProjectsDbContext db)
    : IQueryHandler<GetProjectStageQuery, ProjectStageResponse>
{
    public async Task<Result<ProjectStageResponse>> HandleAsync(GetProjectStageQuery query, CancellationToken ct = default)
    {
        var response = await db.ProjectStages.AsNoTracking()
            .Where(s => s.Id == query.StageId && s.ProjectId == query.ProjectId)
            .Select(ToResponse())
            .FirstOrDefaultAsync(ct);

        return response is null ? Result.Failure<ProjectStageResponse>(Domain.StageErrors.NotFound) : response;
    }

    internal static System.Linq.Expressions.Expression<Func<Domain.ProjectStage, ProjectStageResponse>> ToResponse() => s =>
        new ProjectStageResponse(
            s.Id, s.ProjectId, s.Name, s.Description, s.Order, ProjectsEnumMapping.ToContracts(s.Status),
            s.ProgressPercent, s.CompletedTasks, s.TotalTasks, s.Cost);
}

internal sealed class GetProjectStagesByProjectQueryHandler(ProjectsDbContext db)
    : IQueryHandler<GetProjectStagesByProjectQuery, IReadOnlyList<ProjectStageResponse>>
{
    public async Task<Result<IReadOnlyList<ProjectStageResponse>>> HandleAsync(
        GetProjectStagesByProjectQuery query, CancellationToken ct = default)
    {
        List<ProjectStageResponse> stages = await db.ProjectStages.AsNoTracking()
            .Where(s => s.ProjectId == query.ProjectId)
            .OrderBy(s => s.Order)
            .Select(GetProjectStageQueryHandler.ToResponse())
            .ToListAsync(ct);

        return Result.Success<IReadOnlyList<ProjectStageResponse>>(stages);
    }
}
