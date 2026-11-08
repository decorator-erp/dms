namespace Crm.Domain;

/// <summary>
/// P2-A: the client portal's visibility rule, in one place, used by the client routes, the office preview (the same
/// code path) and the notification handlers.
/// <list type="number">
/// <item><b>Two gates.</b> The client sees an item only while its section is switched on AND the item is shared (an
/// active <see cref="CrmPortalShare"/>). No settings row means every section is off.</item>
/// <item><b>Update attachments.</b> An image or document attached to a published update is also visible through
/// that update while the Updates section is on — the office attached it to a post it published for the client.</item>
/// <item><b>Progress</b> (% and the stage timeline) needs only its switch. The timeline lists every stage until any
/// stage is shared; from then on, only the shared ones, still in stage order.</item>
/// </list>
/// The source must still exist too; that half is the reader's (an item that no longer resolves is left out).
/// </summary>
internal static class CrmPortalVisibility
{
    public static PortalSection SectionOf(PortalShareKind kind) => kind switch
    {
        PortalShareKind.Document => PortalSection.Documents,
        PortalShareKind.Image => PortalSection.Gallery,
        PortalShareKind.SitePhoto => PortalSection.SiteSurveys,
        PortalShareKind.Stage => PortalSection.Progress,
        PortalShareKind.Task => PortalSection.Tasks,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public static bool IsOn(CrmPortalSettings? settings, PortalSection section) => settings?.IsOn(section) ?? false;

    public static bool CanSeeShare(CrmPortalSettings? settings, CrmPortalShare share, bool attachedToVisibleUpdate = false) =>
        share.IsActive
        && (IsOn(settings, share.Section) || attachedToVisibleUpdate && IsOn(settings, PortalSection.Updates));

    public static bool CanSeeUpdate(CrmPortalSettings? settings, CrmPortalUpdate update) =>
        update.IsActive && update.IsPublished && IsOn(settings, PortalSection.Updates);

    /// <summary>The stages the timeline shows, in the order of <paramref name="stageIdsInOrder"/>, and whether that is
    /// limited to the shared ones. A shared stage that no longer exists does not count.</summary>
    public static (IReadOnlyList<Guid> StageIds, bool LimitedToShared) TimelineStages(
        IReadOnlyList<Guid> stageIdsInOrder, IReadOnlyCollection<Guid> sharedStageIds)
    {
        List<Guid> shared = [.. stageIdsInOrder.Where(sharedStageIds.Contains)];
        return shared.Count == 0 ? (stageIdsInOrder, false) : (shared, true);
    }

    public static bool IsImage(string? contentType) =>
        contentType is not null && contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
}
