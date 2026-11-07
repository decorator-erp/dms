using Microsoft.EntityFrameworkCore;
using SharedKernel;
using Sites.Contracts;
using Sites.Contracts.Records;
using Sites.Domain;
using Sites.Infrastructure.Persistence;

namespace Sites.Infrastructure.Services;

/// <inheritdoc cref="ISiteModuleValidator"/>
internal sealed class SiteModuleValidator(SitesDbContext db) : ISiteModuleValidator
{
    private const string Prefix = "sites.validation";

    public async Task<Result> ValidateAsync(
        Guid moduleId, IReadOnlyList<SiteValueRequest> values, CancellationToken ct = default) =>
        (await ValidateManyAsync([new SiteModuleValueSet(moduleId, values)], ct))[0];

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Result>> ValidateManyAsync(
        IReadOnlyList<SiteModuleValueSet> sets, CancellationToken ct = default)
    {
        if (sets.Count == 0)
        {
            return [];
        }

        var moduleIds = sets.Select(s => s.ModuleId).Distinct().ToList();

        // Every named module's field set, with each item's declared type and limits. One round trip for
        // the whole grid (S7); the schema is small (a module has a handful of fields).
        var fieldsByModule = (await (
            from link in db.SiteModuleItems.AsNoTracking()
            join item in db.SiteItems.AsNoTracking() on link.ItemId equals item.Id
            where moduleIds.Contains(link.ModuleId) && link.IsActive && item.IsActive
            select new Field(
                link.ModuleId,
                link.ItemId,
                link.IsRequired,
                item.Code,
                item.DataType,
                item.MinValue,
                item.MaxValue,
                item.MaxLength,
                item.RegexPattern)).ToListAsync(ct))
            .GroupBy(f => f.ModuleId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // B-S-L5: no active fields is a legitimate form (a new module, or every field detached), not a
        // missing module. Only a module that does not exist is ModuleNotFound; otherwise an empty value set
        // passes and any supplied value is reported as an unknown item below. One query for every module
        // without fields.
        var fieldless = moduleIds.Where(id => !fieldsByModule.ContainsKey(id)).ToList();
        HashSet<Guid> existing = fieldless.Count == 0
            ? []
            : (await db.SiteModules.Where(m => fieldless.Contains(m.Id)).Select(m => m.Id).ToListAsync(ct))
                .ToHashSet();

        // Defect B7's fix. The backend issues one AnyAsync PER Select value, inside the loop -- the
        // exact N+1 its own domain service carries a comment about having removed ("a 50-row grid of 25
        // fields spent over a thousand round trips holding it open"), reintroduced one layer up. One
        // batched load instead, for the whole grid, and only for the items a Select value was actually
        // supplied for.
        var optionItemIds = sets
            .SelectMany(set => set.Values
                .Where(v => v.OptionId.HasValue
                            && fieldsByModule.TryGetValue(set.ModuleId, out List<Field>? f)
                            && f.Exists(x => x.ItemId == v.ItemId))
                .Select(v => v.ItemId))
            .Distinct()
            .ToList();

        Dictionary<Guid, HashSet<Guid>> activeOptionsByItem = optionItemIds.Count == 0
            ? []
            : (await db.SiteItemOptions.AsNoTracking()
                    .Where(o => optionItemIds.Contains(o.ItemId) && o.IsActive)
                    .Select(o => new { o.ItemId, o.Id })
                    .ToListAsync(ct))
                .GroupBy(o => o.ItemId)
                .ToDictionary(g => g.Key, g => g.Select(o => o.Id).ToHashSet());

        var results = new List<Result>(sets.Count);

        foreach (SiteModuleValueSet set in sets)
        {
            if (fieldsByModule.TryGetValue(set.ModuleId, out List<Field>? fields))
            {
                results.Add(Validate(fields, set.Values, activeOptionsByItem));
            }
            else
            {
                results.Add(existing.Contains(set.ModuleId)
                    ? Validate([], set.Values, activeOptionsByItem)
                    : SiteErrors.ModuleNotFound);
            }
        }

        return results;
    }

    private sealed record Field(
        Guid ModuleId,
        Guid ItemId,
        bool IsRequired,
        string Code,
        Sites.Domain.SiteItemDataType DataType,
        decimal? MinValue,
        decimal? MaxValue,
        int? MaxLength,
        string? RegexPattern);

    /// <summary>The pure rule, for one record's values against its module's fields.</summary>
    private static Result Validate(
        List<Field> fields, IReadOnlyList<SiteValueRequest> values,
        Dictionary<Guid, HashSet<Guid>> activeOptionsByItem)
    {
        var byItem = fields.ToDictionary(f => f.ItemId);

        var supplied = new HashSet<Guid>();
        var errors = new List<Error>();

        foreach (SiteValueRequest value in values)
        {
            if (!byItem.TryGetValue(value.ItemId, out Field? field))
            {
                errors.Add(Error.BadRequest(
                    $"{Prefix}.UnknownItem",
                    $"Item {value.ItemId} is not a field of this module."));
                continue;
            }

            if (!supplied.Add(value.ItemId))
            {
                errors.Add(Error.BadRequest(
                    $"{Prefix}.{field.Code}.Duplicate",
                    $"'{field.Code}' was supplied more than once. A record holds at most one value per item."));
                continue;
            }

            string? text = string.IsNullOrWhiteSpace(value.ValueText) ? null : value.ValueText.Trim();

            int populated = (value.ValueNumber.HasValue ? 1 : 0)
                          + (text is not null ? 1 : 0)
                          + (value.ValueBool.HasValue ? 1 : 0)
                          + (value.ValueDate.HasValue ? 1 : 0)
                          + (value.OptionId.HasValue ? 1 : 0);

            // Mirrors ck_srv_one_value, so the caller gets a 400 naming the field rather than a 500 from
            // the constraint.
            if (populated != 1)
            {
                errors.Add(Error.BadRequest(
                    $"{Prefix}.{field.Code}.OneValue",
                    $"'{field.Code}' must carry exactly one value, not {populated}."));
                continue;
            }

            // The populated column must be the one the item's type demands. No CHECK can express this:
            // it would have to read the site_items row.
            bool matchesType = field.DataType switch
            {
                Sites.Domain.SiteItemDataType.Number => value.ValueNumber.HasValue,
                Sites.Domain.SiteItemDataType.Text => text is not null,
                Sites.Domain.SiteItemDataType.Bool => value.ValueBool.HasValue,
                Sites.Domain.SiteItemDataType.Select => value.OptionId.HasValue,
                Sites.Domain.SiteItemDataType.Date => value.ValueDate.HasValue,
                _ => false,
            };

            if (!matchesType)
            {
                errors.Add(Error.BadRequest(
                    $"{Prefix}.{field.Code}.TypeMismatch",
                    $"'{field.Code}' expects a {field.DataType} value."));
                continue;
            }

            if (value.ValueNumber is { } number)
            {
                if (field.MinValue is { } min && number < min)
                {
                    errors.Add(Error.BadRequest(
                        $"{Prefix}.{field.Code}.Min", $"'{field.Code}' must be at least {min}."));
                }

                if (field.MaxValue is { } max && number > max)
                {
                    errors.Add(Error.BadRequest(
                        $"{Prefix}.{field.Code}.Max", $"'{field.Code}' must be at most {max}."));
                }
            }

            if (text is not null)
            {
                if (field.MaxLength is { } maxLength && text.Length > maxLength)
                {
                    errors.Add(Error.BadRequest(
                        $"{Prefix}.{field.Code}.MaxLength",
                        $"'{field.Code}' must be {maxLength} characters or fewer."));
                }

                // .NET regex, never a Postgres CHECK -- the dialects differ, so a database-side ~ would
                // reject strings this accepted.
                if (field.RegexPattern is { } pattern && !SafeIsMatch(text, pattern))
                {
                    errors.Add(Error.BadRequest(
                        $"{Prefix}.{field.Code}.Pattern",
                        $"'{field.Code}' is not in the expected format."));
                }
            }

            if (value.OptionId is { } optionId)
            {
                bool belongs = activeOptionsByItem.TryGetValue(value.ItemId, out HashSet<Guid>? options)
                              && options.Contains(optionId);

                if (!belongs)
                {
                    errors.Add(Error.BadRequest(
                        $"{Prefix}.{field.Code}.Option",
                        $"That option does not belong to '{field.Code}', or is no longer active."));
                }
            }
        }

        // Only detectable across the whole set: a missing value has no row of its own to fail on.
        foreach (Field field in fields.Where(f => f.IsRequired && !supplied.Contains(f.ItemId)))
        {
            errors.Add(Error.BadRequest(
                $"{Prefix}.{field.Code}.Required", $"'{field.Code}' is required by this module."));
        }

        // Every failure at once, never the first. The caller is a grid -- a surveyor fixing one cell per
        // round trip is not a usable capture form.
        return errors.Count == 0 ? Result.Success() : Result.Failure(new ValidationError([.. errors]));
    }

    /// <summary>
    /// Timed out, and a malformed pattern treated as "no pattern" — both deliberately PASS. The pattern
    /// is admin-supplied through the form builder and evaluated on the surveyor's write path, so neither
    /// a catastrophic backtrack nor a typo may block every submission for that field. Mirrors
    /// <c>SiteItem.ValidateValue</c>'s own guard one layer down.
    /// </summary>
    private static bool SafeIsMatch(string input, string pattern)
    {
        try
        {
            return System.Text.RegularExpressions.Regex.IsMatch(
                input, pattern, System.Text.RegularExpressions.RegexOptions.None,
                TimeSpan.FromMilliseconds(200));
        }
        catch (ArgumentException)
        {
            return true;
        }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            return true;
        }
    }
}
