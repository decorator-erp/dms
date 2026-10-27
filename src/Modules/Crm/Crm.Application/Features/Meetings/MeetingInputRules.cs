using Crm.Contracts.Meetings;
using FluentValidation;

namespace Crm.Application.Features.Meetings;

/// <summary>B-10 (L1): the shared meeting input rules. Times must be UTC: an Unspecified or Local
/// <see cref="DateTime"/> used to reach Npgsql and fail as a 500.</summary>
internal static class MeetingInputRules
{
    public const int MaxSlots = 10;
    public const int TitleMaxLength = 256;
    public const int LocationMaxLength = 512;
    public const int MinDurationMinutes = 5;
    public const int MaxDurationMinutes = 480;

    public static IRuleBuilderOptions<T, DateTime> MustBeUtc<T>(this IRuleBuilder<T, DateTime> rule)
        => rule.Must(d => d.Kind == DateTimeKind.Utc).WithMessage("{PropertyName} must be a UTC time (ending in 'Z').");

    public static IRuleBuilderOptions<T, IReadOnlyList<CrmMeetingSlotRequest>?> AreValidSlots<T>(
        this IRuleBuilder<T, IReadOnlyList<CrmMeetingSlotRequest>?> rule)
        => rule.Must(s => s is null || s.Count <= MaxSlots).WithMessage($"At most {MaxSlots} slots are allowed.")
            .Must(s => s is null || s.All(IsValid))
            .WithMessage("Every slot must be in UTC and end after it starts.");

    private static bool IsValid(CrmMeetingSlotRequest slot)
        => slot is not null
            && slot.StartsAtUtc.Kind == DateTimeKind.Utc && slot.EndsAtUtc.Kind == DateTimeKind.Utc
            && slot.EndsAtUtc > slot.StartsAtUtc;
}
