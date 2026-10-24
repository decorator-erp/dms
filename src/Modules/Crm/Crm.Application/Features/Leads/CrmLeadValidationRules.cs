using FluentValidation;

namespace Crm.Application.Features.Leads;

/// <summary>The lead's field rules, kept in step with <c>CrmLeadConfiguration</c>'s column lengths so an
/// over-long value is a 400 here instead of a <c>DbUpdateException</c> (500) at save — the same reason
/// <c>CrmBriefHeadDataValidator</c> exists for the brief head. Shared by create and update, whose commands
/// carry the same fields but share no type.</summary>
internal static class CrmLeadValidationRules
{
    public const int NameMaxLength = 128;
    public const int EmailMaxLength = 256;
    public const int PhoneMaxLength = 32;
    public const int NotesMaxLength = 2000;

    public static IRuleBuilderOptions<T, string> LeadName<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().MaximumLength(NameMaxLength);

    public static IRuleBuilderOptions<T, string?> LeadEmail<T>(this IRuleBuilder<T, string?> rule) =>
        rule.MaximumLength(EmailMaxLength).EmailAddress();
}
