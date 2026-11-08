using SharedKernel;

namespace Crm.Domain;

/// <summary>
/// P2-A: one row per project — which client portal sections are switched on. Every switch starts off, for new and
/// existing projects alike (a project with no row reads as all off), so nothing reaches the client until the office
/// turns it on. A switch is only half of the rule: an item also has to be shared (<see cref="CrmPortalShare"/>);
/// see <see cref="CrmPortalVisibility"/>.
/// </summary>
internal sealed class CrmPortalSettings : Entity, IAuditable
{
    public Guid ProjectId { get; private set; }
    public bool ShowProgress { get; private set; }
    public bool ShowTasks { get; private set; }
    public bool ShowDocuments { get; private set; }
    public bool ShowGallery { get; private set; }
    public bool ShowSiteSurveys { get; private set; }
    public bool ShowUpdates { get; private set; }

    private CrmPortalSettings() { }

    public static CrmPortalSettings Create(Guid projectId) => new() { ProjectId = projectId };

    public void Set(bool progress, bool tasks, bool documents, bool gallery, bool siteSurveys, bool updates)
    {
        ShowProgress = progress;
        ShowTasks = tasks;
        ShowDocuments = documents;
        ShowGallery = gallery;
        ShowSiteSurveys = siteSurveys;
        ShowUpdates = updates;
    }

    public bool IsOn(PortalSection section) => section switch
    {
        PortalSection.Progress => ShowProgress,
        PortalSection.Tasks => ShowTasks,
        PortalSection.Documents => ShowDocuments,
        PortalSection.Gallery => ShowGallery,
        PortalSection.SiteSurveys => ShowSiteSurveys,
        PortalSection.Updates => ShowUpdates,
        _ => false,
    };
}
