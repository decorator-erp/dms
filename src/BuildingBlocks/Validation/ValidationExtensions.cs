using System.Linq.Expressions;
using FluentValidation;

namespace BuildingBlocks.Validation;

public static class ValidationExtensions
{
    /// <summary>
    /// For an optional string property: when a value is provided it must not be empty/whitespace.
    /// Replaces the repeated <c>RuleFor(x =&gt; x.Prop).NotEmpty().When(x =&gt; x.Prop is not null)</c> pattern.
    /// </summary>
    public static IRuleBuilderOptions<T, string?> RuleForOptional<T>(
        this AbstractValidator<T> validator,
        Expression<Func<T, string?>> selector)
    {
        Func<T, string?> getValue = selector.Compile();

        return validator.RuleFor(selector)
            .NotEmpty()
            .When(x => getValue(x) is not null);
    }
}
