using BuildingBlocks.Messaging;

namespace Crm.Contracts.Notes;

/// <summary>Internal-only — never surfaced on the Portal. <c>CrmClientNote</c>'s own doc comment says
/// the backend's access layer denies contact actors outright; the endpoint layer (G9-14) enforces the
/// same by never registering a Portal route for this feature.</summary>
public sealed record CrmClientNoteResponse(
    Guid Id, Guid ClientId, Guid? ProjectId, string Content, Guid WrittenById);

public sealed record CreateCrmClientNoteCommand(
    Guid ClientId, Guid? ProjectId, string Content, Guid WrittenById) : ICommand<Guid>;

public sealed record UpdateCrmClientNoteCommand(Guid NoteId, string Content) : ICommand;

public sealed record DeleteCrmClientNoteCommand(Guid NoteId) : ICommand;

public sealed record GetCrmClientNotesByClientQuery(Guid ClientId) : IQuery<IReadOnlyList<CrmClientNoteResponse>>;
