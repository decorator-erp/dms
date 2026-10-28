namespace Crm.Application.Features;

/// <summary>D-16 / A-F9: the upper bound on every CRM paged query's <c>PageNumber</c>. Far beyond any real
/// list, and low enough that <c>(PageNumber - 1) * PageSize</c> can't overflow an <c>int</c> skip.</summary>
internal static class CrmPaging
{
    public const int MaxPageNumber = 1_000_000;
}
