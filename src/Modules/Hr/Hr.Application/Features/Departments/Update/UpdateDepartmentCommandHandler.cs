using System.Globalization;
using System.Text.RegularExpressions;
using BuildingBlocks.Messaging;
using FluentValidation;
using Hr.Contracts.Departments;
using Hr.Domain;
using Hr.Domain.Errors;
using Hr.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using SharedKernel;

namespace Hr.Application.Features.Departments.Update;

internal sealed class UpdateDepartmentCommandValidator : AbstractValidator<UpdateDepartmentCommand>
{
    public UpdateDepartmentCommandValidator()
    {
        RuleFor(x => x.DepartmentId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
    }
}

internal sealed class UpdateDepartmentCommandHandler(HrDbContext db) : ICommandHandler<UpdateDepartmentCommand>
{
    public async Task<Result> HandleAsync(UpdateDepartmentCommand command, CancellationToken ct = default)
    {
        var department = await db.Departments
            .Where(x => x.Id == command.DepartmentId)
            .Select(x => new { x.Id, x.Name, x.Description })
            .SingleOrDefaultAsync(ct);
        if (department is null)
        {
            return DepartmentErrors.NotFound;
        }
        
        Action<UpdateSettersBuilder<Department>>? settersAction = null;
        
        if (department.Name != command.Name)
        {
            string newName = NameCorrection(command.Name);
            if (await db.Departments.AnyAsync(x => x.Name == newName, ct))
            {
                return DepartmentErrors.DuplicatedName(command.Name);
            }
            settersAction = setters => setters.SetProperty(x => x.Name, newName);
        }

        if (!string.IsNullOrEmpty(command.Description) && department.Description != command.Description)
        {
            settersAction = settersAction is null ? 
                setters => setters.SetProperty(x => x.Description, command.Description) : 
                settersAction + (setters => setters.SetProperty(x => x.Description, command.Description));
        }

        if (settersAction is not null)
        {
            int affectedRows = await db.Departments
                .Where(x => x.Id == command.DepartmentId)
                .ExecuteUpdateAsync(settersAction, ct);
        }

        return Result.Success();
    }
    private static string NameCorrection(string s)
    {
        string name = s.Trim();

        name = Regex.Replace(name, @"\s+", " ");
        name = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(name.ToLower());
        return name;
    }
}
