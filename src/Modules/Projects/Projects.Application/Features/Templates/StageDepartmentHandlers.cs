using BuildingBlocks.Messaging;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts.Templates;
using Projects.Infrastructure.Persistence;
using SharedKernel;
using Domain = Projects.Domain;

namespace Projects.Application.Features.Templates;

internal sealed class AddStageDepartmentCommandValidator : AbstractValidator<AddStageDepartmentCommand>
{
    public AddStageDepartmentCommandValidator()
    {
        RuleFor(x => x.StageId).NotEmpty();
        RuleFor(x => x.DepartmentId).NotEmpty();
    }
}

internal sealed class AddStageDepartmentCommandHandler(ProjectsDbContext db) : ICommandHandler<AddStageDepartmentCommand>
{
    public async Task<Result> HandleAsync(AddStageDepartmentCommand command, CancellationToken ct = default)
    {
        var stageExists = await db.Stages.AsNoTracking().AnyAsync(s => s.Id == command.StageId, ct);
        if (!stageExists)
            return Domain.TemplateEntityErrors.StageNotFound;

        var alreadyExists = await db.StageDepartments.AsNoTracking()
            .AnyAsync(sd => sd.StageId == command.StageId && sd.DepartmentId == command.DepartmentId, ct);
        if (alreadyExists)
            return Result.Success();

        db.StageDepartments.Add(Domain.StageDepartment.Create(command.StageId, command.DepartmentId));
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

internal sealed class RemoveStageDepartmentCommandHandler(ProjectsDbContext db) : ICommandHandler<RemoveStageDepartmentCommand>
{
    public async Task<Result> HandleAsync(RemoveStageDepartmentCommand command, CancellationToken ct = default)
    {
        var stageDepartment = await db.StageDepartments
            .FirstOrDefaultAsync(sd => sd.StageId == command.StageId && sd.DepartmentId == command.DepartmentId, ct);
        if (stageDepartment is null)
            return Result.Success();

        stageDepartment.Remove();
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

internal sealed class GetStageDepartmentsQueryHandler(ProjectsDbContext db)
    : IQueryHandler<GetStageDepartmentsQuery, IReadOnlyList<StageDepartmentResponse>>
{
    public async Task<Result<IReadOnlyList<StageDepartmentResponse>>> HandleAsync(
        GetStageDepartmentsQuery query, CancellationToken ct = default)
    {
        List<StageDepartmentResponse> departments = await db.StageDepartments.AsNoTracking()
            .Where(sd => sd.StageId == query.StageId && sd.IsActive)
            .Select(sd => new StageDepartmentResponse(sd.StageId, sd.DepartmentId))
            .ToListAsync(ct);

        return Result.Success<IReadOnlyList<StageDepartmentResponse>>(departments);
    }
}
