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

namespace Hr.Application.Features.Departments.Update;

internal sealed class UpdateDepartmentCommandValidator : AbstractValidator<UpdateDepartmentCommand>
{
    public UpdateDepartmentCommandValidator()
    {
        RuleFor(x => x.DepartmentId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);
    }
}

/// <summary>
/// N8: a PUT replaces the name and the description (empty or missing clears it, where it used to be
/// impossible to clear). The duplicate check runs on the corrected name, among active departments other
/// than this one, so a rename that only changes casing or spacing is not a "duplicate" of itself. The
/// write goes through the tracked entity (audited, <c>UpdatedAtUtc</c> set), not
/// <c>ExecuteUpdateAsync</c>, which the audit interceptor never saw. A deleted department is 404.
/// </summary>
internal sealed class UpdateDepartmentCommandHandler(HrDbContext db) : ICommandHandler<UpdateDepartmentCommand>
{
    public async Task<Result> HandleAsync(UpdateDepartmentCommand command, CancellationToken ct = default)
    {
        Department? department = await db.Departments.FirstOrDefaultAsync(d => d.Id == command.DepartmentId, ct);
        if (department is null)
        {
            return DepartmentErrors.NotFound;
        }

        string newName = NameCorrection(command.Name);
        if (await db.Departments.AnyAsync(d => d.Name == newName && d.Id != command.DepartmentId, ct))
        {
            return DepartmentErrors.DuplicatedName(command.Name);
        }

        department.Update(newName, string.IsNullOrWhiteSpace(command.Description) ? null : command.Description);
        return await DepartmentSave.SaveAsync(db, command.Name, ct);
    }

    private static string NameCorrection(string s)
    {
        string name = s.Trim();

        name = Regex.Replace(name, @"\s+", " ");
        name = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(name.ToLower());
        return name;
    }
}
