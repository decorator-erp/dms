using Crm.Domain;
using Xunit;

namespace Crm.UnitTests;

/// <summary>P2-A: the two gates. A client sees an item only while its section is switched on AND the item is
/// shared (an active share). The stage timeline needs only the Progress switch and is limited to the shared stages
/// once any stage is shared.</summary>
public class CrmPortalVisibilityTests
{
    private static readonly Guid Project = Guid.NewGuid();
    private static readonly Guid Employee = Guid.NewGuid();

    [Fact]
    public void A_new_project_has_every_section_switched_off()
    {
        var settings = CrmPortalSettings.Create(Project);

        foreach (PortalSection section in Enum.GetValues<PortalSection>())
        {
            Assert.False(settings.IsOn(section));
            Assert.False(CrmPortalVisibility.IsOn(settings, section));
        }

        // No settings row at all is the same as all off.
        Assert.False(CrmPortalVisibility.IsOn(null, PortalSection.Documents));
    }

    [Theory]
    [InlineData(1, 3)] // Document -> Documents
    [InlineData(2, 4)] // Image -> Gallery
    [InlineData(3, 5)] // SitePhoto -> SiteSurveys
    [InlineData(4, 1)] // Stage -> Progress
    [InlineData(5, 2)] // Task -> Tasks
    public void Each_kind_belongs_to_one_section(int kind, int section) =>
        Assert.Equal((PortalSection)section, CrmPortalVisibility.SectionOf((PortalShareKind)kind));

    [Fact]
    public void A_shared_item_is_visible_only_while_its_section_is_on()
    {
        CrmPortalShare share = Share(PortalShareKind.Document);
        var settings = CrmPortalSettings.Create(Project);

        Assert.False(CrmPortalVisibility.CanSeeShare(settings, share));

        settings.Set(progress: true, tasks: true, documents: false, gallery: true, siteSurveys: true, updates: true);
        Assert.False(CrmPortalVisibility.CanSeeShare(settings, share));

        settings.Set(progress: false, tasks: false, documents: true, gallery: false, siteSurveys: false, updates: false);
        Assert.True(CrmPortalVisibility.CanSeeShare(settings, share));
    }

    [Fact]
    public void An_unshared_item_is_never_visible()
    {
        CrmPortalShare share = Share(PortalShareKind.Image);
        CrmPortalSettings settings = AllOn();

        share.Unshare();

        Assert.False(share.IsActive);
        Assert.False(CrmPortalVisibility.CanSeeShare(settings, share));
        Assert.False(CrmPortalVisibility.CanSeeShare(settings, share, attachedToVisibleUpdate: true));
    }

    [Fact]
    public void An_update_attachment_is_visible_through_the_updates_section_even_when_its_own_section_is_off()
    {
        CrmPortalShare image = Share(PortalShareKind.Image);
        var settings = CrmPortalSettings.Create(Project);
        settings.Set(progress: false, tasks: false, documents: false, gallery: false, siteSurveys: false, updates: true);

        Assert.False(CrmPortalVisibility.CanSeeShare(settings, image));
        Assert.True(CrmPortalVisibility.CanSeeShare(settings, image, attachedToVisibleUpdate: true));

        settings.Set(progress: false, tasks: false, documents: false, gallery: false, siteSurveys: false, updates: false);
        Assert.False(CrmPortalVisibility.CanSeeShare(settings, image, attachedToVisibleUpdate: true));
    }

    [Fact]
    public void Only_a_published_live_update_is_visible_and_only_while_updates_is_on()
    {
        CrmPortalUpdate update = CrmPortalUpdate.Create(Project, "Tiles in", "All done.", Employee, []).Value!;
        CrmPortalSettings settings = AllOn();

        Assert.False(CrmPortalVisibility.CanSeeUpdate(settings, update));

        Assert.True(update.Publish(DateTime.UtcNow).IsSuccess);
        Assert.True(CrmPortalVisibility.CanSeeUpdate(settings, update));
        Assert.False(CrmPortalVisibility.CanSeeUpdate(CrmPortalSettings.Create(Project), update));

        update.Delete();
        Assert.False(CrmPortalVisibility.CanSeeUpdate(settings, update));
    }

    [Fact]
    public void Publishing_twice_is_a_conflict()
    {
        CrmPortalUpdate update = CrmPortalUpdate.Create(Project, "Tiles in", "All done.", Employee, []).Value!;
        Assert.True(update.Publish(DateTime.UtcNow).IsSuccess);

        Assert.Equal(CrmPortalShareErrors.AlreadyPublished, update.Publish(DateTime.UtcNow).Error);
    }

    [Fact]
    public void The_timeline_shows_every_stage_until_a_stage_is_shared_then_only_the_shared_ones()
    {
        Guid first = Guid.NewGuid(), second = Guid.NewGuid(), third = Guid.NewGuid();
        Guid[] all = [first, second, third];

        (IReadOnlyList<Guid> none, bool limited) = CrmPortalVisibility.TimelineStages(all, []);
        Assert.Equal(all, none);
        Assert.False(limited);

        (IReadOnlyList<Guid> some, bool limitedNow) = CrmPortalVisibility.TimelineStages(all, [third, first]);
        Assert.Equal([first, third], some); // the timeline keeps stage order, not share order
        Assert.True(limitedNow);

        // A shared stage that no longer exists does not empty the timeline into "nothing".
        (IReadOnlyList<Guid> stale, bool limitedStale) = CrmPortalVisibility.TimelineStages(all, [Guid.NewGuid()]);
        Assert.Equal(all, stale);
        Assert.False(limitedStale);
    }

    [Theory]
    [InlineData("image/png", true)]
    [InlineData("IMAGE/JPEG", true)]
    [InlineData("application/pdf", false)]
    [InlineData("", false)]
    public void An_image_is_an_image_content_type(string contentType, bool expected) =>
        Assert.Equal(expected, CrmPortalVisibility.IsImage(contentType));

    [Fact]
    public void A_document_share_follows_the_latest_version_by_default_and_other_kinds_never_do()
    {
        var root = Guid.NewGuid();
        CrmPortalShare document = CrmPortalShare.Create(
            Project, PortalShareKind.Document, Guid.NewGuid(), root, followLatest: true, "  ", null, 0, Employee, DateTime.UtcNow).Value!;
        Assert.True(document.FollowLatest);
        Assert.Equal(root, document.RootDocumentId);
        Assert.Null(document.ClientTitle); // blank is no title

        CrmPortalShare task = CrmPortalShare.Create(
            Project, PortalShareKind.Task, Guid.NewGuid(), null, followLatest: true, "Tiles", null, 0, Employee, DateTime.UtcNow).Value!;
        Assert.False(task.FollowLatest);

        Assert.Equal(CrmPortalShareErrors.InvalidItem, CrmPortalShare.Create(
            Project, PortalShareKind.Document, Guid.NewGuid(), null, true, null, null, 0, Employee, DateTime.UtcNow).Error);
        Assert.Equal(CrmPortalShareErrors.TitleTooLong, CrmPortalShare.Create(
            Project, PortalShareKind.Task, Guid.NewGuid(), null, true, new string('x', CrmPortalShare.TitleMaxLength + 1), null, 0,
            Employee, DateTime.UtcNow).Error);
    }

    [Fact]
    public void An_update_keeps_its_attachments_in_the_given_order_once_each()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        CrmPortalUpdate update = CrmPortalUpdate.Create(Project, "News", "Body", Employee, [b, a, b]).Value!;

        Assert.Equal([b, a], update.Attachments.OrderBy(x => x.SortOrder).Select(x => x.ShareId));

        Assert.True(update.Edit("News", "Body", [a]).IsSuccess);
        Assert.Equal([a], update.Attachments.Select(x => x.ShareId));
        Assert.Equal(CrmPortalShareErrors.UpdateTitleRequired, CrmPortalUpdate.Create(Project, " ", "Body", Employee, []).Error);
    }

    private static CrmPortalShare Share(PortalShareKind kind) => CrmPortalShare.Create(
        Project, kind, Guid.NewGuid(), kind is PortalShareKind.Document or PortalShareKind.Image ? Guid.NewGuid() : null,
        followLatest: true, null, null, 0, Employee, DateTime.UtcNow).Value!;

    private static CrmPortalSettings AllOn()
    {
        var settings = CrmPortalSettings.Create(Project);
        settings.Set(progress: true, tasks: true, documents: true, gallery: true, siteSurveys: true, updates: true);
        return settings;
    }
}
