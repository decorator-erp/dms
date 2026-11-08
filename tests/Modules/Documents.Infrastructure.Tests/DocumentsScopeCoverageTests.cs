using BuildingBlocks.Messaging;
using Documents.Contracts;
using Xunit;

namespace Documents.Infrastructure.Tests;

/// <summary>
/// BF-1 prerequisite: the Documents counterpart of <c>SiteScopeCoverageTests</c>. The 404 that
/// <c>DocumentsScopeDecorator</c> answers for a project the caller is not on is only as good as the set of
/// messages that opt in, and opting in is one interface on a record — forgettable, and invisible when
/// forgotten: the route simply serves any project's files to anyone holding the claim. That was the state
/// of this module until the scope landed, so the check is not hypothetical.
///
/// <para>The exemption is "carries none of the five ids", not an allow-list: the file-template messages are
/// a global admin catalog with no project, and nobody can add a project-bound message beside them and claim
/// the same exemption.</para>
/// </summary>
public class DocumentsScopeCoverageTests
{
    private static readonly (string Property, Type Marker)[] Keys =
    [
        ("ProjectId", typeof(IDocumentsProjectScoped)),
        ("ProjectStageId", typeof(IDocumentsStageScoped)),
        ("ProjectTaskId", typeof(IDocumentsTaskScoped)),
        ("FolderId", typeof(IDocumentsFolderScoped)),
        ("DocumentId", typeof(IDocumentsDocumentScoped)),
    ];

    private static readonly Type[] Messages = [.. typeof(DocumentsPermissions).Assembly.GetTypes()
        .Where(t => t is { IsAbstract: false, IsInterface: false })
        .Where(IsMessage)];

    /// <summary>A non-nullable id among the five means the message is about a row inside one project.</summary>
    [Fact]
    public void EveryMessageCarryingAProjectBoundId_ImplementsIDocumentsScoped()
    {
        string[] offenders = [.. Messages
            .Where(t => Keys.Any(k => t.GetProperty(k.Property)?.PropertyType == typeof(Guid)))
            .Where(t => !typeof(IDocumentsScoped).IsAssignableFrom(t))
            .Select(t => t.Name)
            .OrderBy(n => n, StringComparer.Ordinal)];

        Assert.Empty(offenders);
    }

    /// <summary>The converse: a scoped message names exactly ONE key, and carries the id that key resolves
    /// from. Two keys would leave the decorator choosing; none would resolve <c>Guid.Empty</c> and 404 every
    /// call, which looks like a permissions bug rather than the wiring bug it is.</summary>
    [Fact]
    public void EveryScopedMessage_ImplementsExactlyOneKeyedMarker_AndCarriesItsId()
    {
        string[] offenders = [.. Messages
            .Where(t => typeof(IDocumentsScoped).IsAssignableFrom(t))
            .Where(t =>
            {
                var markers = Keys.Where(k => k.Marker.IsAssignableFrom(t)).ToList();
                return markers.Count != 1 || t.GetProperty(markers[0].Property)?.PropertyType != typeof(Guid);
            })
            .Select(t => t.Name)
            .OrderBy(n => n, StringComparer.Ordinal)];

        Assert.Empty(offenders);
    }

    /// <summary>The vacuity check: the walks above must actually see the module's messages.</summary>
    [Fact]
    public void TheGuardActuallyScansMessages()
    {
        // 24 project-bound: project 4, stage 4, task 5, folder 5, document 6. The other 7 are the file-template
        // catalog (5 commands, 2 queries), which belongs to no project.
        Assert.Equal(31, Messages.Length);
        Assert.Equal(24, Messages.Count(t => typeof(IDocumentsScoped).IsAssignableFrom(t)));
    }

    private static bool IsMessage(Type type) =>
        typeof(IBaseCommand).IsAssignableFrom(type)
        || type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IQuery<>));
}
