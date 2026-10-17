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
    public CreateDepartmentCommandValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
}

internal sealed class CreateDepartmentCommandHandler(HrDbContext db) : ICommandHandler<CreateDepartmentCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreateDepartmentCommand command, CancellationToken ct = default)
    {
        string name = NameCorrection(command.Name);

        if (await db.Departments.AnyAsync(d => d.Name == name, ct))
        {
            return DepartmentErrors.DuplicatedName(command.Name);
        }

        var department = Department.Create(name, command.Description);
        db.Departments.Add(department);
        await db.SaveChangesAsync(ct);

        return department.Id;
    }
    private static string NameCorrection(string s)
    {
        string name = s.Trim();

        name = Regex.Replace(name, @"\s+", " ");
        name = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(name.ToLower());
        return name;
    }
}
