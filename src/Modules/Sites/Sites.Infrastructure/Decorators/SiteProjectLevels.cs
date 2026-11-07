using Projects.Contracts;
using Sites.Contracts.Documents;
using Sites.Contracts.Records;
using Sites.Contracts.Surveys;

namespace Sites.Infrastructure.Decorators;

/// <summary>
/// C6: the least project level each site write needs, checked by <see cref="SiteScopeDecorator"/> through
/// <see cref="IProjectAccessLevelReader"/>. Capturing a survey — its data, records, values and documents, and
/// submitting it — is Editor work; deciding, reopening and deleting a survey is Manager work. Kept here rather
/// than on the Sites.Contracts messages because Sites.Contracts does not reference Projects.Contracts.
/// <see cref="SiteScopeRegistration"/> refuses to boot with an <c>ISiteScoped</c> command missing from this map.
/// </summary>
internal static class SiteProjectLevels
{
    internal static readonly IReadOnlyDictionary<Type, ProjectMemberAccess> Required = new Dictionary<Type, ProjectMemberAccess>
    {
        [typeof(UpdateSiteCommand)] = ProjectMemberAccess.Editor,
        [typeof(SubmitSiteCommand)] = ProjectMemberAccess.Editor,
        [typeof(AddSiteRecordCommand)] = ProjectMemberAccess.Editor,
        [typeof(UpdateSiteRecordCommand)] = ProjectMemberAccess.Editor,
        [typeof(DeleteSiteRecordCommand)] = ProjectMemberAccess.Editor,
        [typeof(SetSiteRecordValuesCommand)] = ProjectMemberAccess.Editor,
        [typeof(BulkSaveSiteRecordsCommand)] = ProjectMemberAccess.Editor,
        [typeof(LinkSiteDocumentCommand)] = ProjectMemberAccess.Editor,
        [typeof(UnlinkSiteDocumentCommand)] = ProjectMemberAccess.Editor,
        [typeof(DecideSiteCommand)] = ProjectMemberAccess.Manager,
        [typeof(ReopenSiteCommand)] = ProjectMemberAccess.Manager,
        [typeof(DeleteSiteCommand)] = ProjectMemberAccess.Manager,
    };

    /// <summary><see cref="CreateSiteCommand"/> is not site-scoped; its handler checks this level itself.</summary>
    internal const ProjectMemberAccess Create = ProjectMemberAccess.Editor;
}
