using System.Globalization;
using System.Text.RegularExpressions;
using BuildingBlocks.Messaging;
using FluentValidation;
using Hr.Contracts.Departments;
using Hr.Domain;
using Hr.Domain.Errors;
using Hr.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Hr.Application.Features.Departments.Create;

internal sealed class CreateDepartmentCommandValidator : AbstractValidator<CreateDepartmentCommand>
{
    public CreateDepartmentCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);

        // N8: the column is 2000; longer used to reach the database and come back a 500.
        RuleFor(x => x.Description).MaximumLength(2000);
    }
}

internal sealed class CreateDepartmentCommandHandler(HrDbContext db) : ICommandHandler<CreateDepartmentCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreateDepartmentCommand command, CancellationToken ct = default)
    {
        string name = NameCorrection(command.Name);

        // N8: among ACTIVE departments (K3's filter), so a deleted department's name is free again; the
        // partial unique index says the same.
        if (await db.Departments.AnyAsync(d => d.Name == name, ct))
        {
            return DepartmentErrors.DuplicatedName(command.Name);
        }

        var department = Department.Create(name, string.IsNullOrWhiteSpace(command.Description) ? null : command.Description);
        db.Departments.Add(department);

        Result saved = await DepartmentSave.SaveAsync(db, command.Name, ct);
        return saved.IsFailure ? saved.Error : department.Id;
    }
    private static string NameCorrection(string s)
    {
        string name = s.Trim();

        name = Regex.Replace(name, @"\s+", " ");
        name = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(name.ToLower(CultureInfo.CurrentCulture));
        return name;
    }
}
